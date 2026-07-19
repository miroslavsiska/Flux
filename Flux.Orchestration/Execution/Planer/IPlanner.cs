using Flux.Orchestration.Model;

namespace Flux.Orchestration.Execution.Planer;

/// <summary>Plans and schedules orchestration scenes and signals for execution.</summary>
public interface IPlanner
{
    /// <summary>Queues a scene (by ID) for planning; does not execute it directly.</summary>
    /// <param name="sceneId">The registered scene ID.</param>
    /// <param name="context">The scene context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the asynchronous operation.</returns>
    Task PlanSceneAsync(string sceneId, SceneContext context, CancellationToken cancellationToken = default);

    /// <summary>Queues a scene for planning; does not execute it directly.</summary>
    /// <param name="scene">The scene metadata.</param>
    /// <param name="context">The scene context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the asynchronous operation.</returns>
    Task PlanSceneAsync(SceneMetadata scene, SceneContext context, CancellationToken cancellationToken = default);

    /// <summary>Queues every scene bound to the given signal for planning.</summary>
    /// <param name="signal">The signal name.</param>
    /// <param name="context">The scene context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the asynchronous operation.</returns>
    Task PlanSignalAsync(string signal, SceneContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a scene SYNCHRONOUSLY to completion: compiles its phase DAG and walks the levels directly through the
    /// scheduler, awaiting each level before the next. Unlike <see cref="PlanSceneAsync(string, SceneContext, CancellationToken)"/>,
    /// it does NOT register the scene in the active-scene set and is not driven by the Tick loop — so it keeps no
    /// per-scene-id state, the same scene id can run concurrently / RECURSE (a phase target may call back into
    /// <see cref="ExecuteSceneAsync(string, SceneContext, bool, CancellationToken)"/>), and the returned task completes
    /// only when every phase has run.
    /// </summary>
    /// <param name="sceneId">The id of a registered scene to execute.</param>
    /// <param name="context">The scene context.</param>
    /// <param name="dryRun">When true, no target is invoked — the levels are walked and the result reports what WOULD run (imagination), with zero side effects.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task<SceneExecutionResult> ExecuteSceneAsync(string sceneId, SceneContext context, bool dryRun = false, CancellationToken cancellationToken = default);

    /// <summary>Executes the given scene synchronously to completion (see the string-id overload).</summary>
    Task<SceneExecutionResult> ExecuteSceneAsync(SceneMetadata scene, SceneContext context, bool dryRun = false, CancellationToken cancellationToken = default);
}
