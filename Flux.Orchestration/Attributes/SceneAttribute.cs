using Flux.Orchestration.Model;

namespace Flux.Orchestration.Attributes;

/// <summary>Declares a class as an orchestration scene and configures its planning and execution.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SceneAttribute : Attribute
{
    /// <summary>The unique scene identifier.</summary>
    public required string Id { get; init; }

    /// <summary>How the scene is planned and executed.</summary>
    public ScenePlanningMode? ScenePlanningMode { get; init; }

    /// <summary>Interval at which the scene is planned.</summary>
    public TimeSpan? PlanningInterval { get; init; }

    /// <summary>Priority of the scene.</summary>
    public int Priority { get; init; } = default;

    /// <summary>Whether phases run in parallel when a phase allows it.</summary>
    [Obsolete("Deprecated in favour of the DAG model (phase-level DependsOn / Reads / Writes). " +
              "Retained only as a fallback when no DAG metadata is declared.", error: false)]
    public bool Parallel { get; init; } = false;

    /// <summary>Whether logging is enabled.</summary>
    public bool Logging { get; init; } = true;

    /// <summary>The scene's description.</summary>
    public string? Description { get; init; }

    /// <summary>Logical category for grouping or classification.</summary>
    public string? Category { get; init; }

    /// <summary>Optional signal bindings that trigger this scene.</summary>
    public string[]? Triggers { get; init; }

    /// <summary>Optional tags for filtering, labeling, or diagnostics.</summary>
    public string[]? Tags { get; set; }
}
