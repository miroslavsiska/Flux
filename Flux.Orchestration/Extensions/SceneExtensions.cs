using Flux.Orchestration.Model;

namespace Flux.Orchestration.Extensions;

/// <summary>Extension methods that filter a <see cref="Scene"/> by phase or category.</summary>
/// <remarks>The original scene is never mutated; each method returns a new instance.</remarks>
public static class SceneExtensions
{
    /// <summary>Returns a new scene containing only the phases with the given ids.</summary>
    /// <param name="original">The scene to filter.</param>
    /// <param name="description">Optional description for the new scene.</param>
    /// <param name="includedPhaseIds">Phase ids to keep.</param>
    /// <returns>A new <see cref="Scene"/> with only the specified phases.</returns>
    public static Scene WithPhases(Scene original, string? description, params string[] includedPhaseIds) =>
        Where(original, p => includedPhaseIds.Contains(p.PhaseId), description);

    /// <summary>Returns a new scene excluding the phases with the given ids.</summary>
    /// <param name="original">The scene to filter.</param>
    /// <param name="description">Optional description for the new scene.</param>
    /// <param name="excludedPhaseIds">Phase ids to drop.</param>
    /// <returns>A new <see cref="Scene"/> without the specified phases.</returns>
    public static Scene WithoutPhases(Scene original, string? description, params string[] excludedPhaseIds)
        => Where(original, p => !excludedPhaseIds.Contains(p.PhaseId), description);

    /// <summary>Returns a new scene containing only phases in the given category.</summary>
    /// <param name="original">The scene to filter.</param>
    /// <param name="category">The category to keep.</param>
    /// <param name="description">Optional description for the new scene.</param>
    /// <returns>A new <see cref="Scene"/> with only phases in the category.</returns>
    public static Scene WithCategory(Scene original, string category, string? description)
        => Where(original, p => p.Category?.Contains(category) == true, description);

    /// <summary>Returns a new scene excluding phases in the given category.</summary>
    /// <param name="original">The scene to filter.</param>
    /// <param name="category">The category to drop.</param>
    /// <param name="description">Optional description for the new scene.</param>
    /// <returns>A new <see cref="Scene"/> without phases in the category.</returns>
    public static Scene WithoutCategory(Scene original, string category, string? description)
        => Where(original, p => p.Category?.Contains(category) != true, description);

    /// <summary>Returns a new scene keeping only the phases matching the predicate.</summary>
    /// <param name="original">The scene to filter.</param>
    /// <param name="predicate">Returns <see langword="true"/> to keep a phase.</param>
    /// <param name="description">Optional description for the new scene.</param>
    /// <returns>A new <see cref="Scene"/> with the matching phases.</returns>
    public static Scene Where(Scene original, Func<ScenePhase, bool> predicate, string? description)
    {
        var filteredPhases = original.Phases.Where(predicate).ToList();
        return new Scene(
            original.Id,
            description,
            filteredPhases,
            original.Triggers,
            original.Category,
            original.Tags,
            original.ScenePlanningMode,
            original.PlanningInterval,
            original.Priority,
            original.Parallel,
            original.Logging
        );
    }
}
