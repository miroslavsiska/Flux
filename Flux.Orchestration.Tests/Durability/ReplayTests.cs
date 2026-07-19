using Flux.Orchestration.Durability;
using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Durability;

/// <summary>
/// Pillar 4 — durable replay: enriched/crash-durable journal, idempotency keys, and deterministic re-application
/// of journaled inputs that reproduces the original planning decisions.
/// </summary>
public class ReplayTests
{
    private const string SceneId = "scene-1";
    private const string PhaseId = "phase-a";
    private const string Signal = "OnStart";

    private static (ISceneMetadataRegistry meta, ITargetRegistry targets, IScheduler scheduler) Mocks()
    {
        var meta = Substitute.For<ISceneMetadataRegistry>();
        var targets = Substitute.For<ITargetRegistry>();
        var scheduler = Substitute.For<IScheduler>();
        scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                 .Returns(Task.CompletedTask);

        var scene = new SceneMetadata(SceneId, null, [new ScenePhaseMetadata(PhaseId)],
            [new SignalBinding { Signal = Signal }], scenePlanningMode: ScenePlanningMode.SnapshotDriven);
        meta.Resolve(SceneId).Returns(scene);
        meta.ResolveBySignal(Signal).Returns([scene]);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { });
        targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
               .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding)]);
        return (meta, targets, scheduler);
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

    private static int CountKind(IReadOnlyList<JournalRecord> records, OrchestrationEventKind kind)
        => records.Count(r => r.Event.Kind == kind);

    // ── FileOrchestrationJournal ────────────────────────────────────────────────

    [Fact]
    public async Task FileJournal_AppendRead_RoundTripsAndResumesSequence()
    {
        var path = Path.Combine(Path.GetTempPath(), "flux-journal-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var corr = Guid.NewGuid();
            {
                var journal = new FileOrchestrationJournal(path);
                await journal.AppendAsync(new JournalEvent(OrchestrationEventKind.SignalReceived, "s", null, corr, "sig",
                    IdempotencyKey: "k1", LogicalTimeSeconds: 1.5));
                await journal.AppendAsync(new JournalEvent(OrchestrationEventKind.Tick, "", null, Guid.Empty, null,
                    TickDeltaSeconds: 0.016, LogicalTimeSeconds: 1.516));
            }

            // Reopen: a fresh instance must resume the sequence past existing records.
            var reopened = new FileOrchestrationJournal(path);
            await reopened.AppendAsync(new JournalEvent(OrchestrationEventKind.ScenePlanned, "s", null, corr, null));

            var records = await reopened.ReadAsync();
            Assert.Equal(3, records.Count);
            Assert.Equal([1, 2, 3], records.Select(r => r.Sequence));
            Assert.Equal("sig", records[0].Event.Detail);
            Assert.Equal("k1", records[0].Event.IdempotencyKey);
            Assert.Equal(1.5, records[0].Event.LogicalTimeSeconds, 6);
            Assert.Equal(0.016, records[1].Event.TickDeltaSeconds, 6);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── Idempotency ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanScene_DuplicateIdempotencyKey_AppliedOnce()
    {
        var (meta, targets, scheduler) = Mocks();
        var journal = new InMemoryOrchestrationJournal();
        await using var planner = new DefaultPlanner(meta, targets, scheduler, NullLogger<DefaultPlanner>.Instance, journal);

        await planner.PlanSceneAsync(SceneId, new SceneContext(), "dup-key");
        await planner.PlanSceneAsync(SceneId, new SceneContext(), "dup-key"); // duplicate — must be ignored
        await planner.PlanSceneAsync(SceneId, new SceneContext(), "other-key");

        var requested = CountKind(journal.Read(), OrchestrationEventKind.ScenePlanRequested);
        Assert.Equal(2, requested); // dup-key once + other-key once
    }

    // ── Deterministic replay ─────────────────────────────────────────────────────

    [Fact]
    public async Task Replay_ReproducesPlanningDecisions()
    {
        // ── Original run: journal inputs + ticks, drive planning externally. ──
        var (meta, targets, scheduler) = Mocks();
        var sourceJournal = new InMemoryOrchestrationJournal();
        await using (var original = new DefaultPlanner(meta, targets, scheduler,
                         NullLogger<DefaultPlanner>.Instance, sourceJournal) { JournalTicks = true })
        {
            await original.PlanSignalAsync(Signal, new SceneContext());
            original.Tick(TimeSpan.FromSeconds(0.016));
            await WaitUntilAsync(() => original.Load.InFlight == 0
                && sourceJournal.Read().Any(r => r.Event.Kind == OrchestrationEventKind.ScenePlanned));
        }

        var source = await sourceJournal.ReadAsync();
        Assert.Contains(source, r => r.Event.Kind == OrchestrationEventKind.ScenePlanned);

        // ── Replay into a fresh planner and verify the decisions match. ──
        var (meta2, targets2, scheduler2) = Mocks();
        var replayJournal = new InMemoryOrchestrationJournal();
        await using var replayPlanner = new DefaultPlanner(meta2, targets2, scheduler2,
            NullLogger<DefaultPlanner>.Instance, replayJournal);
        var replayer = new OrchestrationReplayer(replayPlanner, replayJournal);

        var result = await replayer.ReplayAsync(source);

        Assert.True(result.Verify(), "replay should reproduce the original planning-decision sequence");
        Assert.Equal(result.OriginalPlanned, result.ReproducedPlanned);
        Assert.Equal([SceneId], result.ReproducedPlanned);
    }

    [Fact]
    public async Task Replay_DuplicateIdempotentInput_AppliedOnce()
    {
        // Hand-built source: the same signal twice under one idempotency key, then a tick.
        var corr = Guid.NewGuid();
        var ts = default(DateTimeOffset);
        var source = new List<JournalRecord>
        {
            new(1, ts, new JournalEvent(OrchestrationEventKind.SignalReceived, SceneId, null, corr, Signal, IdempotencyKey: "k")),
            new(2, ts, new JournalEvent(OrchestrationEventKind.SignalReceived, SceneId, null, corr, Signal, IdempotencyKey: "k")),
            new(3, ts, new JournalEvent(OrchestrationEventKind.Tick, "", null, Guid.Empty, null, TickDeltaSeconds: 0.016)),
        };

        var (meta, targets, scheduler) = Mocks();
        var replayJournal = new InMemoryOrchestrationJournal();
        await using var planner = new DefaultPlanner(meta, targets, scheduler,
            NullLogger<DefaultPlanner>.Instance, replayJournal);
        var replayer = new OrchestrationReplayer(planner, replayJournal);

        var result = await replayer.ReplayAsync(source);

        // Despite the duplicate signal, the scene is planned exactly once.
        Assert.Equal([SceneId], result.ReproducedPlanned);
    }

    // ── Value-level event-sourcing ───────────────────────────────────────────────

    [Fact]
    public async Task Replay_ReconstructsResourceState_FromLogWithoutRunningTargets()
    {
        var serializer = new JsonStateSerializer(); // int is a built-in type

        // ── Original run: a real scheduler executes a target that writes a resource; writes get journaled. ──
        var meta = Substitute.For<ISceneMetadataRegistry>();
        var targets = Substitute.For<ITargetRegistry>();
        var scene = new SceneMetadata(SceneId, null, [new ScenePhaseMetadata(PhaseId)],
            scenePlanningMode: ScenePlanningMode.SnapshotDriven);
        meta.Resolve(SceneId).Returns(scene);
        var writeBinding = new MethodBindingInfo(MethodCallSignature.Sync,
            SyncDelegate: (_, ctx, _) => ctx.Resources.Write("score", 42));
        targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
            .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), writeBinding)]);

        var sourceJournal = new InMemoryOrchestrationJournal();
        var realScheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        var context = new SceneContext();

        await using (var original = new DefaultPlanner(meta, targets, realScheduler,
                         NullLogger<DefaultPlanner>.Instance, sourceJournal, store: null, serializer: serializer)
                     { JournalTicks = true })
        {
            await original.PlanSceneAsync(scene, context);
            original.Tick(TimeSpan.FromSeconds(0.016));
            await WaitUntilAsync(() => original.Load.InFlight == 0
                && sourceJournal.Read().Any(r => r.Event.Kind == OrchestrationEventKind.ResourceWritten));
        }

        Assert.Equal(42, context.Resources.Read<int>("score")); // the live, executed state

        var source = await sourceJournal.ReadAsync();

        // ── Replay: reconstruct state from the log with a no-op scheduler (targets are NOT re-run). ──
        var meta2 = Substitute.For<ISceneMetadataRegistry>();
        var targets2 = Substitute.For<ITargetRegistry>();
        meta2.Resolve(SceneId).Returns(scene);
        targets2.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
            .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), writeBinding)]);
        var noopScheduler = Substitute.For<IScheduler>();
        noopScheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var replayJournal = new InMemoryOrchestrationJournal();
        await using var replayPlanner = new DefaultPlanner(meta2, targets2, noopScheduler,
            NullLogger<DefaultPlanner>.Instance, replayJournal);
        var replayer = new OrchestrationReplayer(replayPlanner, replayJournal, serializer);

        var result = await replayer.ReplayAsync(source);

        // Decisions reproduced AND resource value recovered purely from the journal.
        Assert.True(result.Verify());
        Assert.True(result.TryGetResource(context.CorrelationId, "score", out var value));
        Assert.Equal(42, Assert.IsType<int>(value));
    }
}
