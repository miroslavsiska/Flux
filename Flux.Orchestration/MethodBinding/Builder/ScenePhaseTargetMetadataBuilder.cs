using Flux.Orchestration.Model;
using System.Diagnostics.CodeAnalysis;

namespace Flux.Orchestration.MethodBinding.Builder;

/// <summary>
/// Default <see cref="IScenePhaseTargetMetadataBuilder"/>: uses an <see cref="IMethodBindingResolver"/> to resolve a
/// target's method into a callable binding and wrap it as <see cref="ScenePhaseTargetMetadata"/>.
/// </summary>
public class ScenePhaseTargetMetadataBuilder : IScenePhaseTargetMetadataBuilder
{
    private readonly IMethodBindingResolver _resolver;
    private readonly MethodResolutionMode _mode;

    public ScenePhaseTargetMetadataBuilder(IMethodBindingResolver resolver, MethodResolutionMode mode)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _mode = mode;
    }

    public ScenePhaseTargetMetadata Build(object? instance, string? methodName, MethodResolutionMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(instance, nameof(instance));
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName, nameof(methodName));

        ScenePhaseTarget target = new(instance, methodName);
        var resolvedMode = mode ?? _mode;
        return Build(target, resolvedMode);
    }

    public ScenePhaseTargetMetadata Build(ScenePhaseTarget target, MethodResolutionMode? mode = null)
    {
        var resolvedMode = mode ?? _mode;
        var method = _resolver.ResolveBinding(target.Type, target.MethodName, resolvedMode);
        if (method is null)
        {
            throw new InvalidOperationException(
                $"Failed to resolve method '{target.MethodName}' on type '{target.Type.FullName}' using mode '{resolvedMode}'.");
        }
        return new ScenePhaseTargetMetadata(target, method);
    }

    public bool TryBuild(object instance, string methodName, out ScenePhaseTargetMetadata? metadata)
    {
        metadata = null;
        ScenePhaseTarget target = new(instance, methodName);
        var method = _resolver.ResolveBinding(target.Type, methodName, _mode);
        if (method is null)
        {
            return false;
        }

        metadata = new ScenePhaseTargetMetadata(target, method);
        return true;
    }
}

