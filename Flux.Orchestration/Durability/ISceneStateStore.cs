namespace Flux.Orchestration.Durability;

/// <summary>
/// Durable store for scene state snapshots. Enables checkpoint/restore: the planner saves a snapshot when a
/// scene's context changes and reloads all snapshots on startup to resume work after a restart.
/// </summary>
/// <remarks>
/// This is snapshot-based durability (persist current state), not command replay (re-run inputs). For an
/// orchestrator whose phases have side effects, restoring state avoids re-executing those side effects on
/// resume. The <see cref="IOrchestrationJournal"/> complements this with an audit/timeline of inputs.
/// </remarks>
public interface ISceneStateStore
{
    /// <summary>Persists (latest-wins per scene) a snapshot of a scene's state.</summary>
    ValueTask SaveAsync(SceneStateSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Loads all persisted scene snapshots (used on startup to restore).</summary>
    ValueTask<IReadOnlyList<SceneStateSnapshot>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes a scene's persisted snapshot.</summary>
    ValueTask RemoveAsync(string sceneId, CancellationToken cancellationToken = default);
}
