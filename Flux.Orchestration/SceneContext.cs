using Flux.Orchestration.Resources;
using System.Collections.Concurrent;

namespace Flux.Orchestration;

/// <summary>Per-run context for a scene: a correlation id plus parameter and resource stores for state and data flow.</summary>
/// <remarks>
/// <see cref="Parameters"/> is backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/> so that parallel
/// phases can safely read and write parameters without external synchronisation. For typed, versioned, torn-read-free
/// inter-phase data prefer <see cref="Resources"/>.
/// </remarks>
public class SceneContext
{
    /// <summary>Identifier correlating related operations within this run.</summary>
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets a thread-safe collection of key-value pairs representing parameters.
    /// Keys are case-insensitive. Safe to access concurrently from parallel phases.
    /// </summary>
    public IDictionary<string, object> Parameters { get; } =
        new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Typed, versioned resource store for race-free inter-phase data sharing. Reads never tear; ordering
    /// between writer and reader phases is enforced by declaring the resource in the phases' <c>Reads</c>/<c>Writes</c>
    /// (the DAG inserts a write-before-read edge). Prefer this over <see cref="Parameters"/> for non-trivial values.
    /// </summary>
    public IResourceStore Resources { get; init; } = new ResourceStore();
}
