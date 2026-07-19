namespace Flux.Orchestration.MethodBinding;

/// <summary>Resolves method bindings from a type, phase/method name, and resolution mode.</summary>
public interface IMethodBindingResolver
{
    /// <summary>Resolves the binding for a type and phase/method name using the given mode.</summary>
    /// <param name="type">The type to resolve against.</param>
    /// <param name="phaseOrMethodName">The phase or method name; null for default/inferred resolution.</param>
    /// <param name="mode">How resolution is performed.</param>
    /// <returns>The resolved binding, or null if none matches.</returns>
    MethodBindingInfo? ResolveBinding(Type type, string? phaseOrMethodName, MethodResolutionMode mode);
}
