using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Flux.Orchestration.Diagnostics;

/// <summary>
/// Central, static diagnostics surface for the orchestrator: a <see cref="System.Diagnostics.Metrics.Meter"/>
/// for metrics and an <see cref="System.Diagnostics.ActivitySource"/> for distributed tracing.
/// </summary>
/// <remarks>
/// Subscribe from OpenTelemetry (or any listener) using the public names:
/// <code>
/// builder.AddMeter(OrchestrationDiagnostics.MeterName)
///        .AddSource(OrchestrationDiagnostics.ActivitySourceName);
/// </code>
/// Instruments are static so they exist once per process and cost nothing when no listener is attached.
/// On the hot path only tagless counters/histograms are recorded (allocation-free); per-scene tags use the
/// stack-only <see cref="TagList"/>, and spans are created only when a listener is present.
/// </remarks>
public static class OrchestrationDiagnostics
{
    /// <summary>Meter name to subscribe to for metrics.</summary>
    public const string MeterName = "Flux.Orchestration";

    /// <summary>ActivitySource name to subscribe to for traces.</summary>
    public const string ActivitySourceName = "Flux.Orchestration";

    private const string Version = "1.0.0";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    private static readonly Meter Meter = new(MeterName, Version);

    // ── Planner / loop ────────────────────────────────────────────────────────

    /// <summary>Planner ticks that actually executed.</summary>
    internal static readonly Counter<long> Ticks =
        Meter.CreateCounter<long>("flux.planner.ticks", "{tick}", "Planner ticks processed.");

    /// <summary>Ticks skipped because another tick was already in flight (loop vs external Tick contention).</summary>
    internal static readonly Counter<long> TicksDropped =
        Meter.CreateCounter<long>("flux.planner.ticks.dropped", "{tick}", "Ticks skipped due to reentrancy.");

    /// <summary>Wall-clock duration of a single tick.</summary>
    internal static readonly Histogram<double> TickDuration =
        Meter.CreateHistogram<double>("flux.planner.tick.duration", "ms", "Duration of a planner tick.");

    // ── Scene planning ────────────────────────────────────────────────────────

    /// <summary>Scenes whose plan was built and dispatched in a tick.</summary>
    internal static readonly Counter<long> ScenesPlanned =
        Meter.CreateCounter<long>("flux.scene.planned", "{scene}", "Scenes planned and dispatched.");

    /// <summary>
    /// Scenes skipped because their previous plan was still executing (the overlap guard). This is the key
    /// <em>overload</em> signal: a rising rate means the orchestrator is not keeping up with its tick rate.
    /// </summary>
    internal static readonly Counter<long> ScenesSkippedBusy =
        Meter.CreateCounter<long>("flux.scene.skipped_busy", "{scene}", "Scenes skipped because still processing (overload).");

    /// <summary>End-to-end duration of planning+dispatching one scene (all DAG levels).</summary>
    internal static readonly Histogram<double> ScenePlanDuration =
        Meter.CreateHistogram<double>("flux.scene.plan.duration", "ms", "Duration of planning a scene across all levels.");

    // ── Scheduler / phases ────────────────────────────────────────────────────

    /// <summary>Phase invocations dispatched (per target).</summary>
    internal static readonly Counter<long> PhasesDispatched =
        Meter.CreateCounter<long>("flux.phase.dispatched", "{phase}", "Phase targets dispatched.");

    /// <summary>Phase retry attempts (excludes the first attempt).</summary>
    internal static readonly Counter<long> PhaseRetries =
        Meter.CreateCounter<long>("flux.phase.retries", "{attempt}", "Phase retry attempts.");

    /// <summary>Phase attempts that hit their timeout.</summary>
    internal static readonly Counter<long> PhaseTimeouts =
        Meter.CreateCounter<long>("flux.phase.timeouts", "{timeout}", "Phase attempts that timed out.");

    /// <summary>Phases that ultimately failed (after exhausting retries).</summary>
    internal static readonly Counter<long> PhaseFailures =
        Meter.CreateCounter<long>("flux.phase.failures", "{failure}", "Phases that failed after all attempts.");

    /// <summary>Phase invocations short-circuited by an open circuit breaker.</summary>
    internal static readonly Counter<long> CircuitOpened =
        Meter.CreateCounter<long>("flux.phase.circuit_open", "{shortcircuit}", "Phase invocations short-circuited by an open circuit.");

    /// <summary>Phases routed to a dead-letter sink instead of throwing.</summary>
    internal static readonly Counter<long> PhasesDeadLettered =
        Meter.CreateCounter<long>("flux.phase.deadlettered", "{deadletter}", "Phases routed to the dead-letter sink.");

    // ── Backpressure / load ─────────────────────────────────────────────────────

    // Process-wide count of scenes whose plan is executing right now. Maintained via Inc/Dec around the plan
    // body and surfaced as an observable gauge — the live backlog signal complementing the skipped-busy counter.
    private static long _scenesInFlight;

    /// <summary>Increments the in-flight scene gauge (a plan started).</summary>
    internal static void IncInFlight() => Interlocked.Increment(ref _scenesInFlight);

    /// <summary>Decrements the in-flight scene gauge (a plan finished).</summary>
    internal static void DecInFlight() => Interlocked.Decrement(ref _scenesInFlight);

    /// <summary>Live number of scenes whose plan is currently executing.</summary>
    internal static readonly ObservableGauge<long> ScenesInFlight =
        Meter.CreateObservableGauge("flux.scene.in_flight", () => Interlocked.Read(ref _scenesInFlight),
            "{scene}", "Scenes whose plan is currently executing (live backlog).");
}
