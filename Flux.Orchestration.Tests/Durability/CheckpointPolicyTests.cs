using Flux.Orchestration.Durability;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Durability;

/// <summary>
/// Checkpoint policy: Manual (explicit only), Periodic (throttled + flushes in-place mutations without a replan),
/// and the default EveryPlan (covered by DurabilityTests).
/// </summary>
public class CheckpointPolicyTests
{
    private const string SceneId = "scene-1";
    private const string PhaseId = "phase-a";

    private readonly ISceneMetadataRegistry _meta = Substitute.For<ISceneMetadataRegistry>();
    private readonly ITargetRegistry _targets = Substitute.For<ITargetRegistry>();
    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();

    public CheckpointPolicyTests()
    {
        _scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                  .Returns(Task.CompletedTask);
    }

    private SceneMetadata BuildScene(ScenePlanningMode mode) =>
        new(SceneId, null, [new ScenePhaseMetadata(PhaseId)], scenePlanningMode: mode);

    private void SetupRegistry(SceneMetadata scene)
    {
        _meta.Resolve(SceneId).Returns(scene);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { });
        _targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
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

    private static int SaveCount(ISceneStateStore store) =>
        store.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ISceneStateStore.SaveAsync));

    // ── Manual ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Manual_DoesNotAutoCheckpoint_ExplicitDoes()
    {
        var store = new InMemorySceneStateStore();
        var scene = BuildScene(ScenePlanningMode.SnapshotDriven);
        SetupRegistry(scene);

        await using var planner = new DefaultPlanner(_meta, _targets, _scheduler,
            NullLogger<DefaultPlanner>.Instance, journal: null, store: store) { Checkpoint = CheckpointPolicy.Manual };

        await planner.PlanSceneAsync(scene, new SceneContext());
        planner.Tick(TimeSpan.Zero);
        await Task.Delay(100); // give the plan time to run

        Assert.Equal(0, store.Count); // Manual: no auto checkpoint

        await planner.CheckpointSceneAsync(SceneId);
        Assert.Equal(1, store.Count); // explicit checkpoint persisted
    }

    // ── Periodic: in-place mutation flushed without a replan ─────────────────────

    [Fact]
    public async Task Periodic_FlushesInPlaceMutation_WithoutReplan()
    {
        var store = new InMemorySceneStateStore();
        var scene = BuildScene(ScenePlanningMode.SnapshotDriven);
        SetupRegistry(scene);

        await using var planner = new DefaultPlanner(_meta, _targets, _scheduler,
            NullLogger<DefaultPlanner>.Instance, journal: null, store: store)
        {
            Checkpoint = CheckpointPolicy.Periodic,
            CheckpointInterval = TimeSpan.FromSeconds(0.05),
        };

        var context = new SceneContext();
        await planner.PlanSceneAsync(scene, context);
        planner.Tick(TimeSpan.FromSeconds(0.1)); // first plan + checkpoint
        await WaitUntilAsync(() => store.Count > 0);
        _scheduler.ClearReceivedCalls();

        // In-place mutation outside any plan — marks the scene dirty via the resource OnWrite hook.
        context.Resources.Write("x", 5);

        // A later tick (interval elapsed) flushes the dirty scene without re-planning it.
        planner.Tick(TimeSpan.FromSeconds(0.1));
        await WaitUntilAsync(() =>
        {
            var s = store.LoadAllAsync().AsTask().Result.SingleOrDefault();
            return s?.Resources is { } r && r.ContainsKey("x");
        });

        var snapshot = (await store.LoadAllAsync()).Single();
        Assert.Equal(5, (int)snapshot.Resources!["x"]);

        // No new plan happened — the resource was persisted purely by the periodic flush.
        await _scheduler.DidNotReceive().ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ── Periodic throttles checkpoint volume vs plan volume ──────────────────────

    [Fact]
    public async Task Periodic_ThrottlesCheckpoints_RelativeToPlans()
    {
        var store = Substitute.For<ISceneStateStore>();
        store.SaveAsync(Arg.Any<SceneStateSnapshot>(), Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);
        store.LoadAllAsync(Arg.Any<CancellationToken>())
             .Returns(ValueTask.FromResult<IReadOnlyList<SceneStateSnapshot>>([]));

        var scene = BuildScene(ScenePlanningMode.Immediate); // plans every tick
        SetupRegistry(scene);

        await using var planner = new DefaultPlanner(_meta, _targets, _scheduler,
            NullLogger<DefaultPlanner>.Instance, journal: null, store: store)
        {
            Checkpoint = CheckpointPolicy.Periodic,
            CheckpointInterval = TimeSpan.FromSeconds(0.05),
        };

        await planner.PlanSceneAsync(scene, new SceneContext());

        // 10 ticks of 0.01s → 10 plans, but checkpoints only every ~0.05s logical time.
        for (int i = 0; i < 10; i++)
        {
            planner.Tick(TimeSpan.FromSeconds(0.01));
            await Task.Delay(15);
        }

        var saves = SaveCount(store);
        Assert.True(saves >= 1, "at least one checkpoint should have happened");
        Assert.True(saves <= 4, $"periodic policy should throttle checkpoints well below 10 plans, got {saves}");
    }
}
