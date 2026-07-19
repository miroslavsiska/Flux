namespace Flux.Orchestration.Execution.Engine;

/// <summary>
/// Invokes phase delegates (sync, <see cref="Task"/>, or <see cref="ValueTask"/>) against a target within a scene context.
/// A null delegate is a no-op.
/// </summary>
public interface IEngine
{
    /// <summary>Invokes a synchronous delegate. No-op if <paramref name="syndDelegate"/> is null.</summary>
    /// <param name="target">The target instance passed to the delegate.</param>
    /// <param name="syndDelegate">The delegate to invoke, or null.</param>
    /// <param name="context">The scene context passed to the delegate.</param>
    /// <param name="ct">Cancellation token observed during invocation.</param>
    void InvokeSync(object target, Action<object, SceneContext, CancellationToken>? syndDelegate, SceneContext context, CancellationToken ct);

    /// <summary>Invokes a <see cref="ValueTask"/> delegate. No-op if <paramref name="valueTaskDelegate"/> is null.</summary>
    /// <param name="target">The target instance passed to the delegate.</param>
    /// <param name="valueTaskDelegate">The delegate to invoke, or null.</param>
    /// <param name="context">The scene context passed to the delegate.</param>
    /// <param name="ct">Cancellation token observed during invocation.</param>
    ValueTask InvokeValueTaskAsync(object target, Func<object, SceneContext, CancellationToken, ValueTask>? valueTaskDelegate, SceneContext context, CancellationToken ct);

    /// <summary>Invokes a <see cref="Task"/> delegate. No-op if <paramref name="taskDelegate"/> is null.</summary>
    /// <param name="target">The target instance passed to the delegate.</param>
    /// <param name="taskDelegate">The delegate to invoke, or null.</param>
    /// <param name="context">The scene context passed to the delegate.</param>
    /// <param name="ct">Cancellation token observed during invocation.</param>
    Task InvokeTaskAsync(object target, Func<object, SceneContext, CancellationToken, Task>? taskDelegate, SceneContext context, CancellationToken ct);
}
