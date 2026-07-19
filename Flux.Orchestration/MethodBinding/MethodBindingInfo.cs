using System.Reflection;

namespace Flux.Orchestration.MethodBinding;

/// <summary>Binds a method signature to its compiled delegate(s) for invocation within a scene context.</summary>
/// <remarks>Exactly one delegate is populated, matching <paramref name="Signature"/>.</remarks>
/// <param name="Signature">The signature style of the bound method.</param>
/// <param name="SyncDelegate">Synchronous invocation delegate; null unless <see cref="MethodCallSignature.Sync"/>.</param>
/// <param name="TaskDelegate">Task-returning invocation delegate; null unless <see cref="MethodCallSignature.Task"/>.</param>
/// <param name="ValueTaskDelegate">ValueTask-returning invocation delegate; null unless <see cref="MethodCallSignature.ValueTask"/>.</param>
/// <param name="Method">The reflected method, for diagnostics. May be null.</param>
public record MethodBindingInfo(
    MethodCallSignature Signature,
    Action<object, SceneContext, CancellationToken>? SyncDelegate = null,
    Func<object, SceneContext, CancellationToken, Task>? TaskDelegate = null,
    Func<object, SceneContext, CancellationToken, ValueTask>? ValueTaskDelegate = null,
    MethodInfo? Method = null // Pro diagnostiku
);
