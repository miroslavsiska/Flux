namespace Flux.Orchestration.Attributes;

/// <summary>Declares a class as a phase of a scene, identified by <see cref="SceneId"/>/<see cref="PhaseId"/>, and configures its execution.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ScenePhaseAttribute : Attribute
{
    /// <summary>The scene identifier.</summary>
    public required string SceneId { get; init; }

    /// <summary>The phase identifier.</summary>
    public required string PhaseId { get; init; }

    /// <summary>Priority of this phase.</summary>
    public int Priority { get; init; } = default;

    /// <summary>Whether this phase can run in parallel.</summary>
    [Obsolete("Deprecated in favour of the DAG model (DependsOn / Reads / Writes). " +
              "Retained only as a fallback when no DAG metadata is declared.", error: false)]
    public bool Parallel { get; init; } = false;

    /// <summary>
    /// Explicit phase dependencies, by PhaseId. This phase starts only after every listed phase completes.
    /// Primary ordering mechanism (supersedes <see cref="Parallel"/>).
    /// </summary>
    public string[]? DependsOn { get; init; }

    /// <summary>
    /// Named resources this phase reads. Write-before-read edges are derived automatically.
    /// </summary>
    public string[]? Reads { get; init; }

    /// <summary>
    /// Named resources this phase writes. Writers of the same resource are ordered by (Priority, PhaseId).
    /// </summary>
    public string[]? Writes { get; init; }

    /// <summary>When true, this phase's targets run sequentially even if the phase shares an execution level.</summary>
    public bool SequentialTargets { get; init; }

    /// <summary>Optional timeout for the phase.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Optional max retry count.</summary>
    public int MaxRetries { get; init; } = 0;

    /// <summary>Whether logging is enabled for this phase.</summary>
    public bool Logging { get; init; } = true;

    /// <summary>Optional description of the phase.</summary>
    public string? Description { get; init; }

    /// <summary>Logical category for grouping or classification.</summary>
    public string? Category { get; init; }

    /// <summary>Optional tags for filtering, labeling, or diagnostics.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Arbitrary phase parameters in key=value form.</summary>
    public string[]? Parameters { get; init; }
}
