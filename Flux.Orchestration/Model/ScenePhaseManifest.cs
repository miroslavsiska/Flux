using Flux.Orchestration.MethodBinding;
using System.Diagnostics.CodeAnalysis;

namespace Flux.Orchestration.Model;

/// <summary>
/// The resolved execution manifest for one scene phase: metadata, configuration, and target details.
/// </summary>
/// <remarks>
/// Pooled and mutable: instances are rented and re-initialized via <see cref="Set"/> each tick so the
/// per-tick work list allocates nothing. It has reference identity — do not retain a manifest reference
/// beyond the scheduling call it was handed to, as it will be reused.
/// </remarks>
public sealed class ScenePhaseManifest
{
    private SceneMetadata _orchestration = null!;
    private ScenePhaseMetadata _phase = null!;
    private ScenePhaseTargetMetadata _targetMetadata = null!;
    private SceneContext _context = null!;
    private IReadOnlyList<string>? _tags;

    /// <summary>
    /// Creates a manifest. Retained for direct construction (e.g. tests); the planner uses the pool + <see cref="Set"/>.
    /// </summary>
    /// <param name="orchestration">The scene metadata.</param>
    /// <param name="phase">The scene phase metadata.</param>
    /// <param name="targetMetadata">The resolved target to be invoked.</param>
    /// <param name="context">The orchestration context.</param>
    public ScenePhaseManifest(SceneMetadata orchestration, ScenePhaseMetadata phase, ScenePhaseTargetMetadata targetMetadata, SceneContext context)
        => Set(orchestration, phase, targetMetadata, context);

    /// <summary>
    /// Re-initializes this (pooled) manifest in place for reuse, avoiding a heap allocation per tick.
    /// </summary>
    [MemberNotNull(nameof(_orchestration), nameof(_phase), nameof(_targetMetadata), nameof(_context))]
    internal void Set(SceneMetadata orchestration, ScenePhaseMetadata phase, ScenePhaseTargetMetadata targetMetadata, SceneContext context)
    {
        _orchestration = orchestration;
        _phase = phase;
        _targetMetadata = targetMetadata;
        _context = context;
        _tags = null; // invalidate the lazy cache — this instance now describes a different phase
    }

    internal SceneMetadata Orchestration => _orchestration;
    internal ScenePhaseMetadata Phase => _phase;
    internal SceneContext Context => _context;
    internal ScenePhaseTargetMetadata TargetMetadata => _targetMetadata;

    /// <summary>
    /// Unique identifier for the orchestration.
    /// </summary>
    public string OrchestrationId => _orchestration.Id;

    /// <summary>
    /// How the orchestration is planned and executed.
    /// </summary>
    public ScenePlanningMode? ScenePlanningMode => _orchestration.ScenePlanningMode;

    /// <summary>
    /// Interval at which the scene is planned.
    /// </summary>
    public TimeSpan? PlanningInterval => _orchestration.PlanningInterval;

    /// <summary>
    /// Priority level of the orchestration.
    /// </summary>
    public int OrchestrationPriority => _orchestration.Priority;

    /// <summary>
    /// Unique identifier for the phase within an orchestration.
    /// </summary>
    public string PhaseId => _phase.PhaseId;

    /// <summary>
    /// The target object that will be invoked during this phase.
    /// </summary>
    public object Target => _targetMetadata.Instance;

    /// <summary>
    /// Runtime type of the target object.
    /// </summary>
    public Type Type => _targetMetadata.Type;

    /// <summary>
    /// The method binding of the target object that will be invoked during this phase.
    /// </summary>
    public MethodBindingInfo MethodBindingInfo => _targetMetadata.MethodBindingInfo;

    /// <summary>
    /// Priority level of the scene phase.
    /// </summary>
    public int Priority => _phase.Priority;

    /// <summary>
    /// Whether the targets of this phase may run concurrently with one another.
    /// </summary>
    /// <remarks>
    /// Governs only intra-phase target concurrency; inter-phase ordering is owned by the compiled DAG plan.
    /// Targets run concurrently unless the phase opts out via
    /// <see cref="Model.Base.ScenePhaseBase.SequentialTargets"/>. The legacy <c>Parallel</c> values are still
    /// honoured as a fallback when no DAG metadata is declared.
    /// </remarks>
    public bool Parallel => _orchestration.Parallel && _phase.Parallel && !_phase.SequentialTargets;

    /// <summary>
    /// Optional timeout for the phase.
    /// </summary>
    public TimeSpan? Timeout => _phase.Timeout;

    /// <summary>
    /// Optional max retry count.
    /// </summary>
    public int MaxRetries => _phase.MaxRetries;

    /// <summary>
    /// Indicates whether logging is enabled for this phase.
    /// </summary>
    public bool Logging => _orchestration.Logging && _phase.Logging;

    /// <summary>
    /// Arbitrary metadata and configuration for this phase.
    /// </summary>
    public Dictionary<string, object> Parameters => _phase.Parameters;

    /// <summary>
    /// Optional description of what this phase does.
    /// </summary>
    public string? Description => $"{_orchestration.Description}, {_phase.Description}";

    /// <summary>
    /// Distinct tags from the orchestration and its phase. Computed lazily so a reused manifest
    /// allocates nothing unless the tags are inspected.
    /// </summary>
    public IReadOnlyList<string>? Tags =>
        _tags ??= (_orchestration.Tags ?? []).Concat(_phase.Tags ?? []).Distinct().ToList();

    /// <summary>
    /// Generates a diagnostic string with detailed information about this execution stage.
    /// </summary>
    public string ToDiagnosticString() =>
        $"[Phase:{PhaseId}] Target={Target.GetType().Name}, Method={MethodBindingInfo.Method?.Name}, Timeout={Timeout}, Parallel={Parallel}, Retries={MaxRetries}";
}
