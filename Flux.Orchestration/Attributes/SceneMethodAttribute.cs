namespace Flux.Orchestration.Attributes;

/// <summary>Binds a method to a scene phase (<see cref="SceneId"/>/<see cref="PhaseId"/>) for discovery and execution.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SceneMethodAttribute : Attribute
{
    /// <summary>The scene identifier.</summary>
    public required string SceneId { get; init; }

    /// <summary>The phase identifier.</summary>
    public required string PhaseId { get; init; }
}
