namespace Flux.Orchestration.Model;

/// <summary>
/// Identifies a single phase within a scene — the (<see cref="SceneId"/>, <see cref="PhaseId"/>) pair used as the
/// registry key for the targets bound to that phase.
/// </summary>
public readonly record struct OrchestrationKey(string SceneId, string PhaseId);

