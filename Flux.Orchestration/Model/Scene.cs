using Flux.Orchestration.Model.Base;
using System.Diagnostics;

namespace Flux.Orchestration.Model;

/// <summary>
/// An orchestration flow: an ordered set of <see cref="ScenePhase"/> phases with optional triggers, identified by its <c>Id</c>.
/// </summary>
[DebuggerDisplay("Scene: {Id}, Phases: {Phases.Count}, Triggers: {Triggers.Count}")]
public sealed class Scene : SceneBase<ScenePhase>
{
    /// <summary>
    /// Converts this scene into its <see cref="SceneMetadata"/> representation.
    /// </summary>
    /// <returns>The metadata representation of this scene.</returns>
    internal SceneMetadata ToMetadata()
        => new (Id, Description, [.. Phases.Select(phase => phase.ToMetadata())], Triggers, Category, Tags, ScenePlanningMode, PlanningInterval, Priority, Parallel, Logging);
    
    /// <summary>
    /// Groups all phase targets by their <see cref="OrchestrationKey"/> (scene <c>Id</c> + phase id).
    /// </summary>
    /// <returns>A read-only map of key to the targets for that phase.</returns>
    internal IReadOnlyDictionary<OrchestrationKey, IReadOnlyList<ScenePhaseTarget>> GetAllTargets()
    {
        return Phases.ToDictionary(
            phase => new OrchestrationKey(Id, phase.PhaseId),
            phase => phase.Targets);
    }
       

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
    public Scene(
        string sceneId,
        string? description,
        IReadOnlyList<ScenePhase> phases,
        IReadOnlyList<SignalBinding>? triggers = null,
         string? category = null,
        IReadOnlyList<string>? tags = null,
        ScenePlanningMode? scenePlanningMode = null,
        TimeSpan? planningInterval = null,
        int priority = default,
        bool parallel = false,
        bool logging = true)
        : base(
            sceneId,
            description,
            phases,
            triggers?.ToList() ?? [],
            category,
            tags,
            scenePlanningMode,
            planningInterval,
            priority,
            parallel,
            logging)
    {
        // NOOP
    }
}
