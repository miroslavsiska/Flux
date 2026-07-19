using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Execution.Planner;

/// <summary>
/// Pillar 2 — backpressure policy (drop-newest vs drop-oldest) and the live load snapshot.
/// Scenes use Immediate mode so every tick attempts a plan and hits the overlap guard deterministically.
/// </summary>
public class BackpressureTests
{
    private const string SceneId = "scene-1";
    private const string PhaseId = "phase-a";

    private readonly ISceneMetadataRegistry _metadataRegistry = Substitute.For<ISceneMetadataRegistry>();
    private readonly ITargetRegistry _targetRegistry = Substitute.For<ITargetRegistry>();
    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();

    private DefaultPlanner NewPlanner(BackpressurePolicy policy) =>
        new(_metadataRegistry, _targetRegistry, _scheduler, NullLogger<DefaultPlanner>.Instance)
        {
            Backpressure = policy,
        };

    private SceneMetadata BuildScene() =>
        new(sceneId: SceneId, description: null, phases: [new ScenePhaseMetadata(PhaseId)],
            scenePlanningMode: ScenePlanningMode.Immediate);

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

    /// <summary>A scheduler whose first call blocks until cancelled; later calls complete immediately.</summary>
    private (Func<bool> firstCancelled, Func<int> calls) BlockingScheduler()
    {
        int calls = 0;
        bool firstCancelled = false;
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                int n = Interlocked.Increment(ref calls);
                if (n == 1)
                {
                    var ct = ci.Arg<CancellationToken>();
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { firstCancelled = true; throw; }
                }
            });
        return (() => Volatile.Read(ref firstCancelled), () => Volatile.Read(ref calls));
    }

    // ── Drop-oldest ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DropOldest_CancelsInFlight_OnOverlap()
    {
        var (firstCancelled, calls) = BlockingScheduler();
        await using var planner = NewPlanner(BackpressurePolicy.DropOldest);
        var scene = BuildScene();
        SetupRegistry(scene);
        await planner.PlanSceneAsync(scene, new SceneContext());

        planner.Tick(TimeSpan.Zero);                 // call #1 starts and blocks
        await WaitUntilAsync(() => calls() == 1);

        planner.Tick(TimeSpan.Zero);                 // overlap → drop-oldest cancels the in-flight plan
        await WaitUntilAsync(firstCancelled);

        Assert.True(firstCancelled());
    }

    // ── Drop-newest (default) ─────────────────────────────────────────────────────

    [Fact]
    public async Task DropNewest_DoesNotCancelInFlight_OnOverlap()
    {
        var (firstCancelled, calls) = BlockingScheduler();
        await using var planner = NewPlanner(BackpressurePolicy.DropNewest);
        var scene = BuildScene();
        SetupRegistry(scene);
        await planner.PlanSceneAsync(scene, new SceneContext());

        planner.Tick(TimeSpan.Zero);                 // call #1 starts and blocks
        await WaitUntilAsync(() => calls() == 1);

        planner.Tick(TimeSpan.Zero);                 // overlap → drop-newest skips, leaves in-flight running
        await Task.Delay(150);

        Assert.False(firstCancelled());              // the oldest plan was NOT cancelled
    }

    // ── Load snapshot ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Load_ReflectsActiveAndInFlightScenes()
    {
        BlockingScheduler();
        await using var planner = NewPlanner(BackpressurePolicy.DropNewest);
        var scene = BuildScene();
        SetupRegistry(scene);

        Assert.Equal(0, planner.Load.ActiveScenes);

        await planner.PlanSceneAsync(scene, new SceneContext());
        Assert.Equal(1, planner.Load.ActiveScenes);
        Assert.Equal(0, planner.Load.InFlight);

        planner.Tick(TimeSpan.Zero);                 // starts the blocking plan
        await WaitUntilAsync(() => planner.Load.InFlight == 1);

        Assert.Equal(1, planner.Load.InFlight);
    }
}
