using Flux.Orchestration.Model.Base;

namespace Flux.Orchestration.Model;

/// <summary>
/// Metadata describing an orchestration flow — its phases, triggers, and execution settings — before it is run.
/// </summary>
public sealed class SceneMetadata : SceneBase<ScenePhaseMetadata>
{
    internal Scene ToScene(IReadOnlyDictionary<string, IReadOnlyList<ScenePhaseTarget>> scenePhaseTargets)
    {
        var phases = new List<ScenePhase>();
        foreach (var metadataPhase in Phases ?? [])
        {
            scenePhaseTargets.TryGetValue(metadataPhase.PhaseId, out var targets);
            var scenePhase = metadataPhase.ToScenePhase(targets ?? []);
            phases.Add(scenePhase);
        }

        return new Scene(
            Id,
            Description,
            phases,
            Triggers,
            Category,
            Tags,
            ScenePlanningMode,
            PlanningInterval,
            Priority,
            Parallel,
            Logging);
    }


    /// <summary>
    /// Creates scene metadata from its id, phases, and optional triggers/metadata.
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
    public SceneMetadata(
        string sceneId,
        string? description,
        IReadOnlyList<ScenePhaseMetadata> phases,
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
            triggers ?? [],
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
