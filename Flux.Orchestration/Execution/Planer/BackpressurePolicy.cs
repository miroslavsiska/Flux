namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// How the planner reacts when a scene is asked to plan again while its previous plan is still running
/// (the per-scene overlap guard). This is the orchestrator's backpressure knob under overload.
/// </summary>
public enum BackpressurePolicy
{
    /// <summary>
    /// Skip the new attempt; the in-flight (oldest) plan runs to completion. The newest state is still picked
    /// up on a later tick once the running plan finishes. This is the default — lowest disruption.
    /// </summary>
    DropNewest,

    /// <summary>
    /// Abort the in-flight (oldest) plan and re-plan the newest state on the next tick. Use when freshness
    /// matters more than completing stale work (e.g. a render scene that must reflect the latest input).
    /// Relies on in-flight cancellation, so a phase only actually stops if it observes its token.
    /// </summary>
    DropOldest,
}

/// <summary>
/// A point-in-time snapshot of planner load, for adaptive hosts that want to throttle input under pressure.
/// </summary>
/// <param name="ActiveScenes">Scenes currently registered with the planner.</param>
/// <param name="InFlight">Scenes whose plan is executing right now.</param>
public readonly record struct OrchestrationLoad(int ActiveScenes, int InFlight);
