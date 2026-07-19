using System.Collections.Concurrent;

namespace Flux.Orchestration.Durability;

/// <summary>
/// In-memory, latest-wins scene state store. Survives nothing beyond the process; useful for tests and as
/// the reference semantics a persistent backend mirrors.
/// </summary>
public sealed class InMemorySceneStateStore : ISceneStateStore
{
    private readonly ConcurrentDictionary<string, SceneStateSnapshot> _snapshots = new(StringComparer.Ordinal);

    public int Count => _snapshots.Count;

    public ValueTask SaveAsync(SceneStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        // Latest-wins, but never regress to an older version under a concurrent save.
        _snapshots.AddOrUpdate(snapshot.SceneId, snapshot,
            (_, existing) => snapshot.Version >= existing.Version ? snapshot : existing);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SceneStateSnapshot>> LoadAllAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<SceneStateSnapshot>>([.. _snapshots.Values]);

    public ValueTask RemoveAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        _snapshots.TryRemove(sceneId, out _);
        return ValueTask.CompletedTask;
    }
}
