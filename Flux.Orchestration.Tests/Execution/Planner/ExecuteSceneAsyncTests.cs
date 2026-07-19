using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Execution.Planner;

/// <summary>
/// Tests for <see cref="DefaultPlanner.ExecuteSceneAsync(SceneMetadata, SceneContext, bool, CancellationToken)"/> —
/// the DIRECT synchronous execution path (no _activeScenes, no Tick): runs to completion when awaited, recurses /
/// runs concurrent same-id instances without collision, and supports a side-effect-free dry-run.
/// </summary>
public class ExecuteSceneAsyncTests : IAsyncLifetime
{
    private readonly ISceneMetadataRegistry _metadataRegistry = Substitute.For<ISceneMetadataRegistry>();
    private readonly ITargetRegistry _targetRegistry = Substitute.For<ITargetRegistry>();
    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();
    private readonly DefaultPlanner _sut;

    private const string SceneId = "scene-1";
    private const string PhaseId = "phase-a";

    public ExecuteSceneAsyncTests()
    {
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                  .Returns(Task.CompletedTask);
        _sut = new DefaultPlanner(_metadataRegistry, _targetRegistry, _scheduler, NullLogger<DefaultPlanner>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _sut.DisposeAsync();

    private static SceneMetadata BuildScene() =>
        new(sceneId: SceneId, description: null, phases: [new ScenePhaseMetadata(PhaseId)], triggers: []);

    private void SetupRegistry(SceneMetadata scene)
    {
        _metadataRegistry.Resolve(scene.Id).Returns(scene);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { });
        var target = new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "Update"), binding);
        _targetRegistry.ResolveMetadata(new OrchestrationKey(scene.Id, PhaseId)).Returns([target]);
    }

    private int ScheduleCalls() =>
        _scheduler.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IScheduler.ScheduleAsync));

    [Fact]
    public async Task ExecuteSceneAsync_RunsSynchronously_WithoutTick()
    {
        var scene = BuildScene();
        SetupRegistry(scene);

        // No Tick(), no loop started — the scene must run purely from the await.
        var result = await _sut.ExecuteSceneAsync(scene, new SceneContext());

        Assert.Equal(1, ScheduleCalls());        // the single non-empty level was dispatched
        Assert.Equal(1, result.Targets);
        Assert.False(result.DryRun);
    }

    [Fact]
    public async Task ExecuteSceneAsync_DryRun_InvokesNothing_ButReportsWhatWouldRun()
    {
        var scene = BuildScene();
        SetupRegistry(scene);

        var result = await _sut.ExecuteSceneAsync(scene, new SceneContext(), dryRun: true);

        Assert.Equal(0, ScheduleCalls());        // imagination: nothing actually ran
        Assert.True(result.DryRun);
        Assert.Equal(1, result.Targets);         // ...but it foresaw the one target that would run
    }

    [Fact]
    public async Task ExecuteSceneAsync_ConcurrentSameSceneId_BothRun_NoCollision()
    {
        // The key property over PlanSceneAsync+Tick (which keys _activeScenes by scene id and would skip-busy the
        // second): the direct path has no per-scene-id state, so two instances of the SAME scene run independently
        // — the foundation for recursion (a phase calling back into the same scene).
        var scene = BuildScene();
        SetupRegistry(scene);

        await Task.WhenAll(
            _sut.ExecuteSceneAsync(SceneId, new SceneContext()),
            _sut.ExecuteSceneAsync(SceneId, new SceneContext()));

        Assert.Equal(2, ScheduleCalls());        // both scheduled — neither was skipped
    }

    [Fact]
    public async Task ExecuteSceneAsync_ByStringId_WhenNotRegistered_Throws()
    {
        _metadataRegistry.Resolve("missing").Returns((SceneMetadata?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ExecuteSceneAsync("missing", new SceneContext()));
    }
}
