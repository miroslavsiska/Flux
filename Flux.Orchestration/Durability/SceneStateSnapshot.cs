using Flux.Orchestration.Model;

namespace Flux.Orchestration.Durability;

/// <summary>
/// A point-in-time, persistable snapshot of a scene's runtime state — enough to rehydrate the scene after a
/// process restart so ticking can resume from where it left off.
/// </summary>
/// <param name="SceneId">Scene identifier (must still be registered to be restorable).</param>
/// <param name="Mode">The scene's planning mode at checkpoint time.</param>
/// <param name="PendingInvalidation">Whether the scene had pending work queued.</param>
/// <param name="CorrelationId">The context's correlation id, preserved across restart.</param>
/// <param name="Parameters">The scene context parameters (their values must be serializable by the configured <see cref="IStateSerializer"/>).</param>
/// <param name="Version">Monotonic checkpoint version (latest-wins per scene).</param>
/// <param name="Resources">
/// The scene context's typed resource values (name→value), captured so a restart restores them without needing
/// the journal. Null/empty when the scene has no resources. Values must be serializable by the configured
/// <see cref="IStateSerializer"/>, same as <paramref name="Parameters"/>.
/// </param>
public sealed record SceneStateSnapshot(
    string SceneId,
    ScenePlanningMode Mode,
    bool PendingInvalidation,
    Guid CorrelationId,
    IReadOnlyDictionary<string, object> Parameters,
    long Version,
    IReadOnlyDictionary<string, object>? Resources = null);
