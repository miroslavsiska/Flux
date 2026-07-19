namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// When the planner persists a scene's state to the durable <c>ISceneStateStore</c>. Only relevant when a store
/// is configured.
/// </summary>
public enum CheckpointPolicy
{
    /// <summary>Checkpoint after every successful plan. Strongest durability, highest write volume — the default.</summary>
    EveryPlan,

    /// <summary>
    /// Checkpoint a scene at most once per <c>CheckpointInterval</c> of logical time. A dirty scene is also flushed
    /// from the tick loop even without a replan, so in-place mutations (e.g. a resource write outside a plan) are
    /// eventually persisted. Bounds write volume under high tick/plan rates.
    /// </summary>
    Periodic,

    /// <summary>Never checkpoint automatically; persist only via explicit <c>CheckpointSceneAsync</c>/<c>CheckpointAllAsync</c>.</summary>
    Manual,
}
