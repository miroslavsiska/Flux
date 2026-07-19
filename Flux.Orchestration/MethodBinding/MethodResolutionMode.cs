namespace Flux.Orchestration.MethodBinding;

/// <summary>
/// Specifies the mode used to resolve methods during processing.
/// </summary>
public enum MethodResolutionMode
{
    /// <summary>
    /// The method resolution will only consider explicitly defined methods.
    /// </summary>
    ExplicitOnly,

    /// <summary>
    /// The method resolution will only consider convention defined methods.
    /// </summary>
    ConventionOnly,

    /// <summary>
    /// The method resolution will consider both - explicit and convention-based resolution are attempted.
    /// </summary>
    Flexible
}
