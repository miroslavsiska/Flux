namespace Flux.Orchestration.Durability;

/// <summary>No-op state store — the default. Its presence lets the planner treat durability as disabled (zero overhead).</summary>
public sealed class NullSceneStateStore : ISceneStateStore
{
    public static readonly NullSceneStateStore Instance = new();

    public ValueTask SaveAsync(SceneStateSnapshot snapshot, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<SceneStateSnapshot>> LoadAllAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<SceneStateSnapshot>>([]);

    public ValueTask RemoveAsync(string sceneId, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
