using Flux.Orchestration.Durability;
using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Durability;

public class DurabilityTests
{
    public record Widget(string Name, int Size);

    private const string SceneId = "s1";
    private const string PhaseId = "p";
    private const string Signal = "OnStart";

    // ── JsonStateSerializer ───────────────────────────────────────────────────

    [Fact]
    public void JsonStateSerializer_RoundTripsRegisteredTypes()
    {
        var serializer = new JsonStateSerializer(typeof(Widget));
        var g = Guid.NewGuid();
        var original = new Dictionary<string, object>
        {
            ["s"] = "hello",
            ["n"] = 42,
            ["g"] = g,
            ["w"] = new Widget("gizmo", 3),
        };

        var restored = serializer.Deserialize(serializer.Serialize(original));

        Assert.Equal("hello", (string)restored["s"]);
        Assert.Equal(42, (int)restored["n"]);
        Assert.Equal(g, (Guid)restored["g"]);
        Assert.Equal(new Widget("gizmo", 3), (Widget)restored["w"]); // record value equality
    }

    [Fact]
    public void JsonStateSerializer_UnregisteredType_Throws()
    {
        var serializer = new JsonStateSerializer(); // Widget not registered
        Assert.Throws<InvalidOperationException>(() =>
            serializer.Serialize(new Dictionary<string, object> { ["w"] = new Widget("x", 1) }));
    }

    // ── FileSceneStateStore ───────────────────────────────────────────────────

    [Fact]
    public async Task FileSceneStateStore_SaveLoadRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "flux-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSceneStateStore(dir, new JsonStateSerializer(typeof(Widget)));
            var snapshot = new SceneStateSnapshot(
                SceneId, ScenePlanningMode.SnapshotDriven, PendingInvalidation: true, Guid.NewGuid(),
                new Dictionary<string, object> { ["count"] = 7, ["w"] = new Widget("a", 2) }, Version: 1);

            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAllAsync();

            var s = Assert.Single(loaded);
            Assert.Equal(SceneId, s.SceneId);
            Assert.True(s.PendingInvalidation);
            Assert.Equal(7, (int)s.Parameters["count"]);
            Assert.Equal(new Widget("a", 2), (Widget)s.Parameters["w"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ── Planner checkpoint + restore ──────────────────────────────────────────

    private static (ISceneMetadataRegistry meta, ITargetRegistry targets, IScheduler scheduler) Mocks()
    {
        var meta = Substitute.For<ISceneMetadataRegistry>();
        var targets = Substitute.For<ITargetRegistry>();
        var scheduler = Substitute.For<IScheduler>();
        scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                 .Returns(Task.CompletedTask);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { });
        targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
               .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding)]);
        return (meta, targets, scheduler);
    }

    private static SceneMetadata Scene(ScenePlanningMode mode = ScenePlanningMode.SnapshotDriven) =>
        new(SceneId, null, [new ScenePhaseMetadata(PhaseId)], [new SignalBinding { Signal = Signal }],
            scenePlanningMode: mode);

    [Fact]
    public async Task Planner_CheckpointsContext_OnPlan()
    {
        var (meta, targets, scheduler) = Mocks();
        var store = new InMemorySceneStateStore();
        var scene = Scene();
        meta.ResolveBySignal(Signal).Returns([scene]);

        await using var planner = new DefaultPlanner(
            meta, targets, scheduler, NullLogger<DefaultPlanner>.Instance, journal: null, store: store);

        var ctx = new SceneContext();
        ctx.Parameters["x"] = 42;
        await planner.PlanSignalAsync(Signal, ctx);
        planner.Tick(TimeSpan.Zero);

        await WaitUntilAsync(() => store.Count > 0);

        var snapshot = (await store.LoadAllAsync()).Single();
        Assert.Equal(SceneId, snapshot.SceneId);
        Assert.Equal(42, (int)snapshot.Parameters["x"]);
    }

    [Fact]
    public async Task Planner_RestoresScene_AndResumesTicking()
    {
        var (meta, targets, scheduler) = Mocks();
        var scene = Scene();
        meta.Resolve(SceneId).Returns(scene);

        // Pre-seed the store as if a previous process had checkpointed with pending work.
        var store = new InMemorySceneStateStore();
        await store.SaveAsync(new SceneStateSnapshot(
            SceneId, ScenePlanningMode.SnapshotDriven, PendingInvalidation: true, Guid.NewGuid(),
            new Dictionary<string, object> { ["x"] = 7 }, Version: 1));

        await using var planner = new DefaultPlanner(
            meta, targets, scheduler, NullLogger<DefaultPlanner>.Instance, journal: null, store: store);

        var restored = await planner.RestoreAsync();
        Assert.Equal(1, restored);

        // The restored scene had pending work → the next tick plans it.
        planner.Tick(TimeSpan.Zero);
        await WaitUntilAsync(() => SchedulerCalls(scheduler) > 0);

        await scheduler.Received(1)
            .ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>());
    }

    // ── Resource state in checkpoints ───────────────────────────────────────────

    [Fact]
    public async Task FileStore_RoundTripsResources()
    {
        var dir = Path.Combine(Path.GetTempPath(), "flux-res-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSceneStateStore(dir, new JsonStateSerializer());
            var snapshot = new SceneStateSnapshot(
                SceneId, ScenePlanningMode.SnapshotDriven, PendingInvalidation: false, Guid.NewGuid(),
                new Dictionary<string, object> { ["p"] = 1 }, Version: 1,
                Resources: new Dictionary<string, object> { ["score"] = 42, ["label"] = "hi" });

            await store.SaveAsync(snapshot);
            var loaded = Assert.Single(await store.LoadAllAsync());

            Assert.NotNull(loaded.Resources);
            Assert.Equal(42, (int)loaded.Resources!["score"]);
            Assert.Equal("hi", (string)loaded.Resources!["label"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Planner_CheckpointsResources_OnPlan()
    {
        // Real scheduler/engine so the target actually writes a resource that the checkpoint must capture.
        var meta = Substitute.For<ISceneMetadataRegistry>();
        var targets = Substitute.For<ITargetRegistry>();
        var scene = Scene();
        meta.Resolve(SceneId).Returns(scene);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync,
            SyncDelegate: (_, ctx, _) => ctx.Resources.Write("score", 99));
        targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
            .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding)]);

        var store = new InMemorySceneStateStore();
        var realScheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        await using var planner = new DefaultPlanner(meta, targets, realScheduler,
            NullLogger<DefaultPlanner>.Instance, journal: null, store: store);

        await planner.PlanSceneAsync(scene, new SceneContext());
        planner.Tick(TimeSpan.Zero);
        await WaitUntilAsync(() => store.Count > 0);

        var snapshot = (await store.LoadAllAsync()).Single();
        Assert.NotNull(snapshot.Resources);
        Assert.Equal(99, (int)snapshot.Resources!["score"]);
    }

    [Fact]
    public async Task Planner_RestoresResources_UsableByNextPlan()
    {
        var meta = Substitute.For<ISceneMetadataRegistry>();
        var targets = Substitute.For<ITargetRegistry>();
        var scene = Scene();
        meta.Resolve(SceneId).Returns(scene);

        int readBack = -1;
        var binding = new MethodBindingInfo(MethodCallSignature.Sync,
            SyncDelegate: (_, ctx, _) => readBack = ctx.Resources.Read<int>("score"));
        targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
            .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding)]);

        // Pre-seed a checkpoint that carries resource state.
        var store = new InMemorySceneStateStore();
        await store.SaveAsync(new SceneStateSnapshot(
            SceneId, ScenePlanningMode.SnapshotDriven, PendingInvalidation: true, Guid.NewGuid(),
            new Dictionary<string, object>(), Version: 1,
            Resources: new Dictionary<string, object> { ["score"] = 123 }));

        var realScheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        await using var planner = new DefaultPlanner(meta, targets, realScheduler,
            NullLogger<DefaultPlanner>.Instance, journal: null, store: store);

        Assert.Equal(1, await planner.RestoreAsync());

        // The restored scene had pending work → the next tick plans it; the target reads the restored resource.
        planner.Tick(TimeSpan.Zero);
        await WaitUntilAsync(() => readBack != -1);

        Assert.Equal(123, readBack);
    }

    private static int SchedulerCalls(IScheduler scheduler) =>
        scheduler.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IScheduler.ScheduleAsync));

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
    }
}
