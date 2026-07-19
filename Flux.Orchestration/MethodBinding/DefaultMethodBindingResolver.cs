using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Flux.Orchestration.MethodBinding;

public sealed class DefaultMethodBindingResolver : IMethodBindingResolver
{
    private Type _voidType = typeof(void);
    private Type _taskType = typeof(Task);
    private Type _valueTaskType = typeof(ValueTask);

    private readonly record struct ResolutionKey(Type Type, string Name);

    private readonly ConcurrentDictionary<ResolutionKey, MethodBindingInfo?> _cache = new();
    private readonly BindingFlags _flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Resolves a method binding for the type and phase/method name using the given mode.</summary>
    /// <remarks>Results are cached per (type, name); name matching is case-insensitive and whitespace-trimmed.</remarks>
    /// <param name="type">The type to resolve against; null returns null.</param>
    /// <param name="phaseOrMethodName">The phase or method name; null/whitespace uses the default binding.</param>
    /// <param name="mode">How the method is selected.</param>
    /// <returns>The resolved binding, or null if none matches.</returns>
    public MethodBindingInfo? ResolveBinding(Type type, string? phaseOrMethodName, MethodResolutionMode mode)
    {
        if (type == null) return null;

        var normalizedName = string.IsNullOrWhiteSpace(phaseOrMethodName)
            ? "default"
            : phaseOrMethodName.Trim().ToLowerInvariant();

        var key = new ResolutionKey(type, normalizedName);

        return _cache.GetOrAdd(key, _ =>
        {
            var method = ResolveMethodInfo(type, phaseOrMethodName, mode);
            if (method == null) return null;

           
            var engineBinding = CompileBinding(method);
            return engineBinding;
        });
    }

    private MethodInfo? ResolveMethodInfo(Type type, string? phaseOrMethodName, MethodResolutionMode mode)
    {
        if (type == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(phaseOrMethodName))
        {
            return TryConventionBased(type, phaseOrMethodName);
        }

        MethodInfo? method = null;

        switch (mode)
        {
            case MethodResolutionMode.ExplicitOnly:
                method = type.GetMethod(phaseOrMethodName!, _flags);
                break;

            case MethodResolutionMode.ConventionOnly:
                method = TryConventionBased(type, phaseOrMethodName);
                break;

            case MethodResolutionMode.Flexible:
                method = type.GetMethod(phaseOrMethodName!, _flags);
                if (method is not null)
                {
                    break;
                }

                method = TryConventionBased(type, phaseOrMethodName);
                break;
        }

        return method;
    }

    private MethodInfo? TryConventionBased(Type type, string? phaseOrMethodName)
    {
        MethodInfo? method = null;
        if (!string.IsNullOrWhiteSpace(phaseOrMethodName))
        {
            var normalized = char.ToUpperInvariant(phaseOrMethodName[0]) + phaseOrMethodName.Substring(1);
            method = 
                type.GetMethod($"On{normalized}Async", _flags) ?? 
                type.GetMethod($"On{normalized}", _flags);
        }

        return method ?? type.GetMethod("OnOrchestrateAsync", _flags) ??
                type.GetMethod("OnOrchestrate", _flags) ??
                 type.GetMethod("OnExecuteAsync", _flags) ??
                type.GetMethod("OnExecute", _flags) ??
                type.GetMethod("HandlePhase", _flags);
    }

    private MethodBindingInfo CompileBinding(MethodInfo method)
    {
        var targetExp = Expression.Parameter(typeof(object), "target");
        var contextExp = Expression.Parameter(typeof(SceneContext), "context");
        var tokenExp = Expression.Parameter(typeof(CancellationToken), "token");
        var castTarget = Expression.Convert(targetExp, method.DeclaringType!);

        var parameters = method.GetParameters().Select(p => {
            if (p.ParameterType == typeof(SceneContext)) return (Expression)contextExp;
            if (p.ParameterType == typeof(CancellationToken)) return (Expression)tokenExp;
            return Expression.Default(p.ParameterType);
        }).ToArray();

        var callExp = Expression.Call(castTarget, method, parameters);

        // SYNC (void)
        if (method.ReturnType == _voidType)
        {
            var lambda = Expression.Lambda<Action<object, SceneContext, CancellationToken>>(
                callExp, targetExp, contextExp, tokenExp).Compile();
            return new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: lambda, Method: method);
        }

        // TASK
        if (method.ReturnType == _taskType)
        {
            var lambda = Expression.Lambda<Func<object, SceneContext, CancellationToken, Task>>(
                callExp, targetExp, contextExp, tokenExp).Compile();
            return new MethodBindingInfo(MethodCallSignature.Task, TaskDelegate: lambda, Method: method);
        }

        // VALUE TASK
        if (method.ReturnType == _valueTaskType)
        {
            var lambda = Expression.Lambda<Func<object, SceneContext, CancellationToken, ValueTask>>(
                callExp, targetExp, contextExp, tokenExp).Compile();
            return new MethodBindingInfo(MethodCallSignature.ValueTask, ValueTaskDelegate: lambda, Method: method);
        }

        throw new NotSupportedException("Nepodporovaný návratový typ.");
    }
}

