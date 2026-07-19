using Flux.Orchestration.Durability;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Durability;

public class OrchestrationJournalTests
{
    private const string SceneId = "s1";
    private const string PhaseId = "p";
    private const string Signal = "OnStart";

    [Fact]
    public async Task InMemoryJournal_AssignsMonotonicGapFreeSequence()
    {
        var journal = new InMemoryOrchestrationJournal();

        await journal.AppendAsync(new JournalEvent(OrchestrationEventKind.SignalReceived, "s", null, Guid.NewGuid(), "a"));
        await journal.AppendAsync(new JournalEvent(OrchestrationEventKind.ScenePlanned, "s", null, Guid.NewGuid(), null));

        var records = journal.Read();
        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Sequence);
        Assert.Equal(2, records[1].Sequence);
        Assert.Equal(OrchestrationEventKind.SignalReceived, records[0].Event.Kind);
    }

    [Fact]
    public async Task Planner_JournalsSignalAndScenePlan()
    {
        var meta = Substitute.For<ISceneMetadataRegistry>();
        var targets = Substitute.For<ITargetRegistry>();
        var scheduler = Substitute.For<IScheduler>();
        scheduler.ScheduleAsync(Arg.Any<IEnumerable<ScenePhaseManifest>>(), Arg.Any<CancellationToken>())
                 .Returns(Task.CompletedTask);

        var journal = new InMemoryOrchestrationJournal();
        await using var planner = new DefaultPlanner(meta, targets, scheduler, NullLogger<DefaultPlanner>.Instance, journal);

        var scene = new SceneMetadata(SceneId, null, [new ScenePhaseMetadata(PhaseId)],
            [new SignalBinding { Signal = Signal }]);
        meta.ResolveBySignal(Signal).Returns([scene]);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { });
        targets.ResolveMetadata(new OrchestrationKey(SceneId, PhaseId))
               .Returns([new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding)]);

        await planner.PlanSignalAsync(Signal, new SceneContext());
        planner.Tick(TimeSpan.Zero);

        // ScenePlanned is appended from the fire-and-forget plan task.
        await WaitUntilAsync(() => journal.Read().Any(r => r.Event.Kind == OrchestrationEventKind.ScenePlanned));

        var kinds = journal.Read().Select(r => r.Event.Kind).ToList();
        Assert.Contains(OrchestrationEventKind.SignalReceived, kinds);
        Assert.Contains(OrchestrationEventKind.ScenePlanned, kinds);
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
}
