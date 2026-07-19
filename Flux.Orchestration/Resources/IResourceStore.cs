namespace Flux.Orchestration.Resources;

/// <summary>
/// A typed, versioned, race-free key→value store for inter-phase data. Replaces ad-hoc use of the untyped,
/// shared <c>SceneContext.Parameters</c> dictionary (which is prone to torn reads for non-primitive values).
/// Reads are lock-free and never torn; ordering between a writer and a reader is the planner's job — declare
/// the resource in a phase's <c>Reads</c>/<c>Writes</c> and the DAG inserts a write-before-read edge.
/// </summary>
public interface IResourceStore
{
    /// <summary>Reads a resource; throws <see cref="KeyNotFoundException"/> if it has never been written.</summary>
    T Read<T>(string name);

    /// <summary>Tries to read a resource. Returns false (and default) if it has never been written.</summary>
    bool TryRead<T>(string name, out T value);

    /// <summary>Writes a resource, creating it if absent. Returns the new monotonic version.</summary>
    long Write<T>(string name, T value);

    /// <summary>
    /// Writes a boxed value using its runtime type, so the resulting cell matches a later typed <see cref="Write{T}"/>.
    /// Used to rehydrate resources from a durable snapshot. Returns the new monotonic version.
    /// </summary>
    long WriteBoxed(string name, object value);

    /// <summary>
    /// Captures the current non-null values as a plain name→value map (boxed) — a point-in-time copy for durable
    /// checkpointing. Null-valued resources are omitted (they cannot be type-serialized).
    /// </summary>
    IReadOnlyDictionary<string, object> Snapshot();

    /// <summary>The current version of a resource, or 0 if it has never been written.</summary>
    long VersionOf(string name);

    /// <summary>Whether a resource has been written at least once.</summary>
    bool Contains(string name);

    /// <summary>The names of all resources currently present.</summary>
    IReadOnlyCollection<string> Names { get; }
}
