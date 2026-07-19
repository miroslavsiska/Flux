using Flux.Orchestration.MethodBinding;

namespace Flux.Orchestration.Options;

/// <summary>
/// Configuration for <c>AddFluxOrchestration</c>. Registered with defaults by that call; override with
/// <c>services.Configure&lt;OrchestrationOptions&gt;(...)</c> before or after it.
/// </summary>
public class OrchestrationOptions
{
    /// <summary>How annotated methods are matched to phases (explicit name, convention, or both). Defaults to <see cref="MethodResolutionMode.Flexible"/>.</summary>
    public MethodResolutionMode MethodResolutionMode { get; set; } = MethodResolutionMode.Flexible;
}
