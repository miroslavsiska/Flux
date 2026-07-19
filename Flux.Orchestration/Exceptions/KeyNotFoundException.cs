using Flux.Orchestration.Model;

namespace Flux.Orchestration.Exceptions;

/// <summary>Thrown when an orchestration key references a phase not defined in the given scene.</summary>
public class OrchestrationKeyNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception for the offending orchestration key.</summary>
    /// <param name="key">The key whose phase/scene identifiers were not found.</param>
    public OrchestrationKeyNotFoundException(OrchestrationKey key)
        : base($"Phase with ID '{key.PhaseId}' is not defined in scene '{key.SceneId}'.") { }
}