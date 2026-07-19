namespace Flux.Orchestration.Model.Base;

/// <summary>
/// Base class for an orchestration scene: its phases, optional signal bindings, and execution metadata.
/// </summary>
public abstract class SceneBase<TPhase> where TPhase : ScenePhaseBase
{
    /// <summary>
    /// Unique identifier for the orchestration scene.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Ordered list of orchestration phases in this orchestration.
    /// </summary>
    public IReadOnlyList<TPhase> Phases => _phases ?? [];
    private List<TPhase>? _phases;

    /// <summary>
    /// Optional triggers that activate this orchestration.
    /// </summary>
    public IReadOnlyList<SignalBinding> Triggers => _triggers ?? [];
    private List<SignalBinding>? _triggers;

    /// <summary>
    /// How the orchestration is planned and executed.
    /// </summary>
    public ScenePlanningMode? ScenePlanningMode { get; init; }

    /// <summary>
    /// Interval at which the scene is planned.
    /// </summary>
    public TimeSpan? PlanningInterval { get; init; }

    /// <summary>
    /// Priority level of the orchestration.
    /// </summary>
    public int Priority { get; init; } = default;

    /// <summary>
    /// Whether phases run in parallel when the phase allows it.
    /// </summary>
    public bool Parallel { get; init; } = false;

    /// <summary>
    /// Whether logging is enabled.
    /// </summary>
    public bool Logging { get; init; } = true;

    /// <summary>
    /// Optional description of the orchestration's purpose.
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

    /// <summary>
    /// Creates a scene from its id, phases, and optional triggers/metadata.
    /// </summary>
    /// <param name="sceneId">Unique scene identifier; not null or empty.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="phases">The scene's phases; not null or empty.</param>
    /// <param name="triggers">Optional triggers; null means none.</param>
    /// <param name="category">Optional grouping category.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="scenePlanningMode">Planning mode.</param>
    /// <param name="planningInterval">Planning interval.</param>
    /// <param name="priority">Priority level.</param>
    /// <param name="parallel">Whether phases may run in parallel.</param>
    /// <param name="logging">Whether logging is enabled.</param>
    public SceneBase(
        string sceneId,
        string? description,
        IEnumerable<TPhase> phases,
        IEnumerable<SignalBinding>? triggers = null,
        string? category = null,
        IReadOnlyList<string>? tags = null,
        ScenePlanningMode? scenePlanningMode = null,
        TimeSpan? planningInterval = null,
        int priority = default,
        bool parallel = false,
        bool logging = true)
    {
        Id = sceneId;
        Description = description;
        _phases = phases.ToList();
        _triggers = triggers?.ToList() ?? [];
        Category = category;
        Tags = tags;
        ScenePlanningMode = scenePlanningMode;
        PlanningInterval = planningInterval;
        Priority = priority;
        Parallel = parallel;
        Logging = logging;
    }

    /// <inheritdoc/>
    public TPhase? GetPhase(string phaseId)
        => Phases.FirstOrDefault(p => p.PhaseId == phaseId);

    /// <inheritdoc/>
    public void AddPhase(TPhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        _phases ??= [];
        _phases.Add(phase);
    }

    /// <inheritdoc/>
    public bool RemovePhase(string phaseId)
    {
        var phase = GetPhase(phaseId);
        if (phase is null)
        {
            return false;
        }
        return RemovePhase(phase);
    }
    /// <inheritdoc/>
    public bool RemovePhase(TPhase phase)
    {
        if (_phases is not null)
        {
            return _phases.Remove(phase);
        }
        return false;
    }

    /// <inheritdoc/>
    public SignalBinding? GetSignal(string signal)
        => Triggers.FirstOrDefault(p => p.Signal == signal);

    /// <inheritdoc/>
    public void AddSignal(SignalBinding signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _triggers ??= [];
        _triggers.Add(signal);
    }

    /// <inheritdoc/>
    public bool RemoveSignal(string signalName)
    {
        var signal = GetSignal(signalName);
        if (signal is null)
        {
            return false;
        }
        return RemoveSignal(signal);
    }

    /// <inheritdoc/>
    public bool RemoveSignal(SignalBinding signal)
    {
        if (_triggers is not null)
        {
            return _triggers.Remove(signal);
        }
        return false;
    }
}
