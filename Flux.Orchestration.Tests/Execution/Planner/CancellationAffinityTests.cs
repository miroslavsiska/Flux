using Flux.Orchestration.Execution;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Execution.Planner;

/// <summary>
/// Pillar 1 — in-flight cancellation (RemoveSceneAsync / StopAsync drain running work) and thread-affinity
/// (a bound scene dispatches on its dedicated thread, including across awaits).
/// </summary>
public class CancellationAffinityTests
{
    private const string SceneId = "scene-1";
    private const string PhaseId = "phase-a";

    private readonly ISceneMetadataRegistry _metadataRegistry = Substitute.For<ISceneMetadataRegistry>();
    private readonly ITargetRegistry _targetRegistry = Substitute.For<ITargetRegistry>();
    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();

    private DefaultPlanner NewPlanner() =>
        new(_metadataRegistry, _targetRegistry, _scheduler, NullLogger<DefaultPlanner>.Instance);

    private SceneMetadata BuildScene(ScenePlanningMode mode = ScenePlanningMode.SnapshotDriven) =>
        new(sceneId: SceneId, description: null, phases: [new ScenePhaseMetadata(PhaseId)],
            scenePlanningMode: mode);

    private void SetupRegistry(SceneMetadata scene)
    {
        _metadataRegistry.Resolve(scene.Id).Returns(scene);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { });
        _targetRegistry.ResolveMetadata(new OrchestrationKey(scene.Id, PhaseId))
            .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding)]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
    }

    // ── In-flight cancellation ────────────────────────────────────────────────

    [Fact]
    public async Task RemoveSceneAsync_CancelsInFlightWork_AndDrains()
    {
        // Scheduler that blocks until its token is cancelled — simulates long-running phase work.
        bool started = false, cancelled = false;
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                started = true;
                var ct = ci.Arg<CancellationToken>();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            });

        await using var planner = NewPlanner();
        var scene = BuildScene();
        SetupRegistry(scene);

        await planner.PlanSceneAsync(scene, new SceneContext());
        planner.Tick(TimeSpan.Zero);                 // launches the (blocking) in-flight plan
        await WaitUntilAsync(() => started);

        // RemoveSceneAsync must cancel the running work and drain within the timeout.
        var removed = await planner.RemoveSceneAsync(SceneId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(removed);
        Assert.True(cancelled);
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightWork()
    {
        bool started = false, cancelled = false;
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                started = true;
                var ct = ci.Arg<CancellationToken>();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            });

        await using var planner = NewPlanner();
        var scene = BuildScene();
        SetupRegistry(scene);

        await planner.PlanSceneAsync(scene, new SceneContext());
        planner.Tick(TimeSpan.Zero);
        await WaitUntilAsync(() => started);

        await planner.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
    }

    [Fact]
    public async Task RemoveSceneAsync_WhenSceneNotActive_ReturnsFalse()
    {
        await using var planner = NewPlanner();
        Assert.False(await planner.RemoveSceneAsync("nope"));
    }

    // ── Thread-affinity ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BoundScene_DispatchesOnAffinityThread()
    {
        using var dispatcher = new DedicatedThreadDispatcher("flux-test-affinity");

        int dispatchThreadId = -1;
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { dispatchThreadId = Environment.CurrentManagedThreadId; return Task.CompletedTask; });

        await using var planner = NewPlanner();
        var scene = BuildScene();
        SetupRegistry(scene);
        planner.BindAffinity(SceneId, dispatcher);

        await planner.PlanSceneAsync(scene, new SceneContext());
        planner.Tick(TimeSpan.Zero);
        await WaitUntilAsync(() => dispatchThreadId != -1);

        Assert.Equal(dispatcher.ManagedThreadId, dispatchThreadId);
    }

    [Fact]
    public async Task DedicatedThreadDispatcher_KeepsAffinityAcrossAwait()
    {
        using var dispatcher = new DedicatedThreadDispatcher();

        int before = 0, after = 0;
        await dispatcher.InvokeAsync(async () =>
        {
            before = Environment.CurrentManagedThreadId;
            await Task.Yield();                       // continuation must resume on the same thread
            after = Environment.CurrentManagedThreadId;
        });

        Assert.Equal(dispatcher.ManagedThreadId, before);
        Assert.Equal(dispatcher.ManagedThreadId, after);
    }

    [Fact]
    public async Task DedicatedThreadDispatcher_PropagatesExceptions()
    {
        using var dispatcher = new DedicatedThreadDispatcher();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.InvokeAsync(() => throw new InvalidOperationException("boom")));
    }
}
