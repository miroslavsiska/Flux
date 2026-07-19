namespace Flux.Orchestration.Model.Base;

/// <summary>
/// Base class for a scene phase, holding common configuration: priority, parallelism, timeout, retries, and metadata.
/// </summary>
public abstract class ScenePhaseBase
{
    /// <summary>
    /// Unique identifier for the phase within an orchestration.
    /// </summary>
    public string PhaseId { get; init; }

    /// <summary>
    /// Gets the priority level associated with the current scene phase.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Indicates whether this phase should run in parallel.
    /// </summary>
    public bool Parallel { get; init; }

    /// <summary>
    /// Optional timeout for the phase.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Optional max retry count.
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// Indicates whether logging is enabled for this phase.
    /// </summary>
    public bool Logging { get; init; }

    /// <summary>
    /// Arbitrary metadata and configuration for this phase.
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// Optional description of what this phase does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Logical category for grouping or classification.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional tags for filtering, labeling, or diagnostics.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    // ── DAG execution model ───────────────────────────────────────────────────
    // The following describe how this phase is ordered relative to other phases of
    // the same scene. They are compiled once (preflight) into a SceneExecutionPlan.

    /// <summary>
    /// Explicit phase dependencies, by <see cref="PhaseId"/>. This phase starts only after every
    /// listed phase has completed. This is the primary ordering mechanism and supersedes
    /// the (deprecated) <see cref="Parallel"/> flag.
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }

    /// <summary>
    /// Named resources this phase reads. Ordering edges are derived automatically:
    /// any phase that writes a resource is ordered before phases that read it (write-before-read).
    /// </summary>
    public IReadOnlyList<string>? Reads { get; init; }

    /// <summary>
    /// Named resources this phase writes. Multiple writers of the same resource are ordered
    /// deterministically by <see cref="Priority"/> then <see cref="PhaseId"/> to avoid write/write races.
    /// </summary>
    public IReadOnlyList<string>? Writes { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the targets of this phase execute one after another even if the
    /// phase itself shares an execution level with other phases. Decouples intra-phase ordering
    /// (targets) from inter-phase ordering (the DAG).
    /// </summary>
    public bool SequentialTargets { get; init; }

    public ScenePhaseBase()
    {
        PhaseId = string.Empty;
        Parameters = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a phase with the given id and optional configuration.
    /// </summary>
    /// <param name="phaseId">Unique phase identifier; not null or empty.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="category">Optional grouping category.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="priority">Priority level; higher runs first.</param>
    /// <param name="parallel">Whether the phase may run in parallel.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="maxRetries">Max retry attempts on failure.</param>
    /// <param name="logging">Whether logging is enabled.</param>
    /// <param name="parameters">Optional parameters; defaults to empty.</param>
    public ScenePhaseBase(
       string phaseId,
       string? description = null,
       string? category = null,
       IReadOnlyList<string>? tags = null,
       int priority = 0,
       bool parallel = false,
       TimeSpan? timeout = null,
       int maxRetries = 0,
       bool logging = true,
       Dictionary<string, object>? parameters = null)
    {
        PhaseId = phaseId;
        Priority = priority;
        Parallel = parallel;
        Timeout = timeout;
        MaxRetries = maxRetries;
        Logging = logging;
        Parameters = parameters ?? new(StringComparer.OrdinalIgnoreCase);
        Description = description;
        Category = category;
        Tags = tags;
    }
}
