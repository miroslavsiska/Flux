using Flux.Orchestration.Diagnostics;
using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;

namespace Flux.Orchestration.Tests.Diagnostics;

/// <summary>
/// Verifies that the observability layer actually emits metrics through the public <see cref="Meter"/>.
/// </summary>
public class OrchestrationDiagnosticsTests
{
    private static ScenePhaseManifest Manifest(MethodBindingInfo binding, bool logging = false, int maxRetries = 0)
    {
        var scene = new SceneMetadata("S", null, [], logging: logging);
        var phase = new ScenePhaseMetadata("P", logging: logging, maxRetries: maxRetries);
        var target = new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding);
        return new ScenePhaseManifest(scene, phase, target, new SceneContext());
    }

    private static MeterListener ListenFor(Dictionary<string, long> sink)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OrchestrationDiagnostics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            lock (sink)
                sink[instrument.Name] = sink.GetValueOrDefault(instrument.Name) + measurement;
        });
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task Dispatching_APhase_EmitsDispatchedCounter()
    {
        var counters = new Dictionary<string, long>();
        using var listener = ListenFor(counters);

        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        var manifest = Manifest(new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { }));

        await scheduler.ScheduleAsync([manifest]);

        lock (counters)
            Assert.True(counters.GetValueOrDefault("flux.phase.dispatched") >= 1);
    }

    [Fact]
    public async Task FailingPhase_WithRetries_EmitsRetryAndFailureCounters()
    {
        var counters = new Dictionary<string, long>();
        using var listener = ListenFor(counters);

        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        var manifest = Manifest(
            new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => throw new InvalidOperationException("boom")),
            maxRetries: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ScheduleAsync([manifest]));

        // Lower-bound assertions: instruments are process-global and other test classes run in parallel,
        // so concurrent tests can only ADD to these counters, never subtract. Our phase contributed
        // exactly 2 retries (attempts 2 and 3) and 1 final failure.
        lock (counters)
        {
            Assert.True(counters.GetValueOrDefault("flux.phase.retries") >= 2);
            Assert.True(counters.GetValueOrDefault("flux.phase.failures") >= 1);
        }
    }

    [Fact]
    public async Task FailingPhase_OnFastPath_EmitsFailureCounter()
    {
        var counters = new Dictionary<string, long>();
        using var listener = ListenFor(counters);

        // No logging, no retries, no resilience → the zero-overhead fast path. A failure must still be counted.
        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        var manifest = Manifest(
            new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => throw new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ScheduleAsync([manifest]));

        lock (counters)
            Assert.True(counters.GetValueOrDefault("flux.phase.failures") >= 1);
    }
}
