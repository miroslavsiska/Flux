using Flux.Orchestration.Model.Base;
using System.Diagnostics;

namespace Flux.Orchestration.Model;

/// <summary>
/// A single phase within a scene: a unique id, its target(s), and optional configuration (priority, timeout, retries, parameters).
/// </summary>
[DebuggerDisplay("PhaseId = {PhaseId}, Phases: {Phases.Count}, Triggers: {Triggers.Count}")]
public sealed class ScenePhase : ScenePhaseBase
{
    /// <summary>
    /// The targets associated with this phase.
    /// </summary>
    public IReadOnlyList<ScenePhaseTarget> Targets => _targets;
    private List<ScenePhaseTarget> _targets = [];

    public ScenePhaseTarget? GetTarget(object instance)
        => Targets.FirstOrDefault(p => p.Instance?.Equals(instance) == true);

    public void AddTarget(ScenePhaseTarget target)
    {
        _targets.Add(target);
    }

    public void AddTarget(object target, string? methodName = null)
    {
        var phaseTarget = new ScenePhaseTarget(target, methodName);
        _targets.Add(phaseTarget);
    }

    public bool RemoveTarget(object instance)
    {
        var target = GetTarget(instance);
        if (target is null)
        {
            return false;
        }
        return RemoveTarget(target);
    }

    public bool RemoveTargets(Type type)
    {
        Targets.Where(t => t.Type == type).ToList()
            .ForEach(t => _targets.Remove(t));
        return false;
    }

    /// <summary>
    /// Converts this phase into its <see cref="ScenePhaseMetadata"/> representation.
    /// </summary>
    /// <returns>The metadata representation of this phase.</returns>
    internal ScenePhaseMetadata ToMetadata()
        => new (PhaseId, Description, Category, Tags, Priority, Parallel, Timeout, MaxRetries, Logging, Parameters)
        {
            DependsOn = DependsOn,
            Reads = Reads,
            Writes = Writes,
            SequentialTargets = SequentialTargets,
        };

    /// <summary>
    /// Creates a phase from its id, targets, and optional configuration.
    /// </summary>
    /// <param name="phaseId">Unique phase identifier; not null or empty.</param>
    /// <param name="targets">The target objects this phase operates on.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="category">Optional grouping category.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="priority">Priority level; higher runs first.</param>
    /// <param name="parallel">Whether the phase may run in parallel.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="maxRetries">Max retry attempts on failure.</param>
    /// <param name="logging">Whether logging is enabled.</param>
    /// <param name="parameters">Optional parameters; defaults to empty.</param>
    public ScenePhase(
       string phaseId,
       IReadOnlyList<ScenePhaseTarget>? targets,
       //string? methodName,
       string? description = null,
       string? category = null,
       IReadOnlyList<string>? tags = null,
       int priority = 0,
       bool parallel = false,
       TimeSpan? timeout = null,
       int maxRetries = 0,
       bool logging = true,
       Dictionary<string, object>? parameters = null)
        : base(
              phaseId,
              description,
              category,
              tags,
              priority,
              parallel,
              timeout,
              maxRetries,
              logging,
              parameters)
    {
        _targets = targets?.ToList() ?? [];
    }
}
