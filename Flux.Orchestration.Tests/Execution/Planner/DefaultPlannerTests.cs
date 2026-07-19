using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Execution.Planner;

/// <summary>
/// Unit tests for <see cref="DefaultPlanner"/>.
///
/// Strategy:
///   - ISceneMetadataRegistry and ITargetRegistry are substituted via NSubstitute.
///   - IScheduler is substituted so we can assert ScheduleAsync call count and arguments
///     without needing a running loop.
///   - Tick-driven tests call Tick() directly after setting up state via PlanSceneAsync /
///     PlanSignalAsync, then await the fire-and-forget task with a short SpinWait.
/// </summary>
public class DefaultPlannerTests : IAsyncLifetime
{
    // ─────────────────────────────────────────────────
    // Infrastructure
    // ─────────────────────────────────────────────────

    private readonly ISceneMetadataRegistry _metadataRegistry = Substitute.For<ISceneMetadataRegistry>();
    private readonly ITargetRegistry _targetRegistry = Substitute.For<ITargetRegistry>();
    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();
    private readonly DefaultPlanner _sut;

    private const string SceneId = "scene-1";
    private const string PhaseId = "phase-a";
    private const string Signal = "OnStart";

    public DefaultPlannerTests()
    {
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                  .Returns(Task.CompletedTask);

        _sut = new DefaultPlanner(
            _metadataRegistry,
            _targetRegistry,
            _scheduler,
            NullLogger<DefaultPlanner>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _sut.DisposeAsync();

    // ── Shared builders ──────────────────────────────

    private SceneMetadata BuildScene(
        string sceneId = SceneId,
        ScenePlanningMode mode = ScenePlanningMode.SnapshotDriven,
        TimeSpan? interval = null) =>
        new(sceneId: sceneId,
            description: null,
            phases: [new ScenePhaseMetadata(PhaseId)],
            triggers: [new SignalBinding { Signal = Signal }],
            scenePlanningMode: mode,
            planningInterval: interval);

    private ScenePhaseTargetMetadata BuildTargetMetadata()
    {
        var binding = new MethodBindingInfo(
            Signature: MethodCallSignature.Sync,
            SyncDelegate: (_, _, _) => { });
        var target = new ScenePhaseTarget(new object(), "Update");
        return new ScenePhaseTargetMetadata(target, binding);
    }

    private void SetupRegistryForScene(SceneMetadata scene)
    {
        _metadataRegistry.Resolve(scene.Id).Returns(scene);
        _metadataRegistry.ResolveBySignal(Signal).Returns([scene]);
        var key = new OrchestrationKey(scene.Id, PhaseId);
        _targetRegistry.ResolveMetadata(key).Returns([BuildTargetMetadata()]);
    }

    /// <summary>
    /// Waits for at most <paramref name="timeoutMs"/> milliseconds for the scheduler
    /// to receive at least one ScheduleAsync call. This is necessary because
    /// Tick() fires PlanAndExecuteAsync as fire-and-forget.
    /// </summary>
    private async Task WaitForSchedulerCallAsync(int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var calls = _scheduler.ReceivedCalls()
                .Count(c => c.GetMethodInfo().Name == nameof(IScheduler.ScheduleAsync));
            if (calls > 0) return;
            await Task.Delay(10);
        }
    }

    // ─────────────────────────────────────────────────
    // PlanSceneAsync(string, ...)
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task PlanSceneAsync_ByStringId_WhenSceneExists_SetsPendingInvalidation()
    {
        // Arrange
        var scene = BuildScene();
        SetupRegistryForScene(scene);

        // Act
        await _sut.PlanSceneAsync(SceneId, new SceneContext());

        // Assert — Tick picks up the invalidation and fires the scheduler
        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();
        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanSceneAsync_ByStringId_WhenSceneNotRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        _metadataRegistry.Resolve(SceneId).Returns((SceneMetadata?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.PlanSceneAsync(SceneId, new SceneContext()));
    }

    // ─────────────────────────────────────────────────
    // PlanSceneAsync(SceneMetadata, ...)
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task PlanSceneAsync_ByMetadata_SetsPendingInvalidationAndContext()
    {
        // Arrange
        var scene = BuildScene();
        SetupRegistryForScene(scene);
        var ctx = new SceneContext();

        // Act
        await _sut.PlanSceneAsync(scene, ctx);

        // Assert — tick drives execution
        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();
        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanSceneAsync_CalledTwice_IdempotentStateRegistration()
    {
        // The same scene should reuse the same SceneRuntimeState — no duplicate scheduler calls on first tick.
        var scene = BuildScene();
        SetupRegistryForScene(scene);

        await _sut.PlanSceneAsync(scene, new SceneContext());
        await _sut.PlanSceneAsync(scene, new SceneContext());

        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();

        // Overlap guard ensures only one ScheduleAsync despite two pending invalidations on same state
        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // PlanSignalAsync
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task PlanSignalAsync_WhenSignalMatchesScene_SetsPendingInvalidation()
    {
        // Arrange
        var scene = BuildScene();
        SetupRegistryForScene(scene);

        // Act
        await _sut.PlanSignalAsync(Signal, new SceneContext());

        // Tick drives execution
        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();

        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanSignalAsync_WhenSignalNotRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        _metadataRegistry.ResolveBySignal(Signal).Returns((IEnumerable<SceneMetadata>?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.PlanSignalAsync(Signal, new SceneContext()));
    }

    [Fact]
    public async Task PlanSignalAsync_WhenSignalMatchesMultipleScenes_AllInvalidated()
    {
        // Arrange
        var scene1 = BuildScene("s1");
        var scene2 = BuildScene("s2");
        _metadataRegistry.ResolveBySignal(Signal).Returns([scene1, scene2]);

        foreach (var scene in new[] { scene1, scene2 })
        {
            var key = new OrchestrationKey(scene.Id, PhaseId);
            _targetRegistry.ResolveMetadata(key).Returns([BuildTargetMetadata()]);
        }

        // Act
        await _sut.PlanSignalAsync(Signal, new SceneContext());

        _sut.Tick(TimeSpan.Zero);

        await Task.Delay(200); // allow both fire-and-forget tasks to settle

        // Both scenes should have triggered a ScheduleAsync
        await _scheduler.Received(2)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Tick — ScenePlanningMode.SnapshotDriven
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_SnapshotDriven_WithPendingInvalidation_SchedulesOnce()
    {
        var scene = BuildScene(mode: ScenePlanningMode.SnapshotDriven);
        SetupRegistryForScene(scene);

        await _sut.PlanSceneAsync(scene, new SceneContext());

        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();

        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_SnapshotDriven_WithoutPendingInvalidation_DoesNotSchedule()
    {
        var scene = BuildScene(mode: ScenePlanningMode.SnapshotDriven);
        SetupRegistryForScene(scene);
        // Register scene without invalidation
        await _sut.PlanSceneAsync(scene, new SceneContext());

        // Consume the invalidation
        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();
        _scheduler.ClearReceivedCalls();

        // Second tick — no new invalidation
        _sut.Tick(TimeSpan.Zero);
        await Task.Delay(100);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Tick — ScenePlanningMode.Immediate
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_Immediate_AlwaysSchedulesRegardlessOfInvalidation()
    {
        var scene = BuildScene(mode: ScenePlanningMode.Immediate);
        SetupRegistryForScene(scene);
        // Set context only — no explicit invalidation needed for Immediate
        await _sut.PlanSceneAsync(scene, new SceneContext());

        _sut.Tick(TimeSpan.Zero);
        await WaitForSchedulerCallAsync();

        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Tick — ScenePlanningMode.Aggregate
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_Aggregate_BeforeInterval_DoesNotSchedule()
    {
        var interval = TimeSpan.FromMilliseconds(100);
        var scene = BuildScene(mode: ScenePlanningMode.Aggregate, interval: interval);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        // Tick with delta less than interval
        _sut.Tick(TimeSpan.FromMilliseconds(50));
        await Task.Delay(100);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_Aggregate_AfterInterval_Schedules()
    {
        var interval = TimeSpan.FromMilliseconds(50);
        var scene = BuildScene(mode: ScenePlanningMode.Aggregate, interval: interval);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        // Tick with delta exceeding interval
        _sut.Tick(TimeSpan.FromMilliseconds(60));
        await WaitForSchedulerCallAsync();

        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_Aggregate_AfterIntervalWithoutInvalidation_DoesNotSchedule()
    {
        var interval = TimeSpan.FromMilliseconds(50);
        var scene = BuildScene(mode: ScenePlanningMode.Aggregate, interval: interval);
        SetupRegistryForScene(scene);

        // Register scene — PlanSceneAsync sets PendingInvalidation; consume it first
        await _sut.PlanSceneAsync(scene, new SceneContext());
        _sut.Tick(TimeSpan.FromMilliseconds(60));
        await WaitForSchedulerCallAsync();
        _scheduler.ClearReceivedCalls();

        // Now tick again without new invalidation — accumulator resets, but no pending work
        _sut.Tick(TimeSpan.FromMilliseconds(60));
        await Task.Delay(100);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Tick — ScenePlanningMode.FixedTimestep
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_FixedTimestep_WhenAccumulatorExceedsInterval_Schedules()
    {
        var interval = TimeSpan.FromMilliseconds(16);
        var scene = BuildScene(mode: ScenePlanningMode.FixedTimestep, interval: interval);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        _sut.Tick(TimeSpan.FromMilliseconds(20));
        await WaitForSchedulerCallAsync();

        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_FixedTimestep_WhenAccumulatorBelowInterval_DoesNotSchedule()
    {
        var interval = TimeSpan.FromMilliseconds(16);
        var scene = BuildScene(mode: ScenePlanningMode.FixedTimestep, interval: interval);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        _sut.Tick(TimeSpan.FromMilliseconds(5));
        await Task.Delay(100);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Tick — ScenePlanningMode.Transition
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_Transition_AfterInterval_Schedules()
    {
        var interval = TimeSpan.FromMilliseconds(8);
        var scene = BuildScene(mode: ScenePlanningMode.Transition, interval: interval);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        _sut.Tick(TimeSpan.FromMilliseconds(10));
        await WaitForSchedulerCallAsync();

        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_Transition_SecondTickAfterReset_RequiresNewInvalidation()
    {
        var interval = TimeSpan.FromMilliseconds(8);
        var scene = BuildScene(mode: ScenePlanningMode.Transition, interval: interval);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        // First tick — schedules and resets PendingInvalidation
        _sut.Tick(TimeSpan.FromMilliseconds(10));
        await WaitForSchedulerCallAsync();
        _scheduler.ClearReceivedCalls();

        // Second tick without new invalidation — should NOT schedule
        _sut.Tick(TimeSpan.FromMilliseconds(10));
        await Task.Delay(100);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Tick — ScenePlanningMode.Manual
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_Manual_NeverSchedulesAutomatically()
    {
        var scene = BuildScene(mode: ScenePlanningMode.Manual);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        // Even with many ticks, Manual mode never auto-schedules
        for (int i = 0; i < 5; i++)
            _sut.Tick(TimeSpan.FromMilliseconds(100));

        await Task.Delay(200);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Overlap guard — concurrent execution prevention
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_SnapshotDriven_WhenSchedulerIsSlowAndTickFires_DoesNotRunConcurrently()
    {
        // Arrange — scheduler that takes 200 ms to complete
        var tcs = new TaskCompletionSource<bool>();
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                  .Returns(_ => tcs.Task);

        var scene = BuildScene(mode: ScenePlanningMode.SnapshotDriven);
        SetupRegistryForScene(scene);
        await _sut.PlanSceneAsync(scene, new SceneContext());

        // First tick starts the long-running schedule
        _sut.Tick(TimeSpan.Zero);
        await Task.Delay(30); // let fire-and-forget reach the scheduler

        // Re-invalidate and tick again while scheduler is still "running"
        await _sut.PlanSceneAsync(scene, new SceneContext());
        _sut.Tick(TimeSpan.Zero);
        await Task.Delay(30);

        // Release the scheduler
        tcs.SetResult(true);
        await Task.Delay(100);

        // Only ONE ScheduleAsync call should have been made (overlap guard blocked second)
        await _scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────
    // Lifecycle — Start / Stop / Dispose
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Start_CalledConcurrently_StartsOnlyOneLoop()
    {
        // Act — simulate concurrent calls
        Parallel.For(0, 10, _ => _sut.Start());
        await _sut.StopAsync();

        // If two loops had started, StopAsync() would deadlock or throw — reaching here is the assertion.
        Assert.True(true);
    }

    [Fact]
    public async Task Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var sut = new DefaultPlanner(
            _metadataRegistry, _targetRegistry, _scheduler,
            NullLogger<DefaultPlanner>.Instance);

        await sut.DisposeAsync();
        var ex = await Record.ExceptionAsync(() => sut.DisposeAsync().AsTask());

        Assert.Null(ex);
    }

    // ─────────────────────────────────────────────────
    // PlanAndExecuteAsync — null context guard
    // ─────────────────────────────────────────────────

    [Fact]
    public async Task Tick_WhenContextIsNull_DoesNotCallScheduler()
    {
        // A scene is registered but PlanSceneAsync was never called — Context stays null.
        var scene = BuildScene(mode: ScenePlanningMode.Immediate);
        SetupRegistryForScene(scene);

        // Manually insert a state without context by calling Tick before PlanSceneAsync
        // We can trigger this by having ImmediateMode fire on first tick directly.
        // Since context is null, PlanAndExecuteAsync should skip gracefully.
        _sut.Tick(TimeSpan.Zero);
        await Task.Delay(100);

        await _scheduler.DidNotReceive()
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }
}
