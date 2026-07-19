using System.Runtime.CompilerServices;

namespace Flux.Orchestration.Execution.Engine;

/// <summary>Default orchestration engine: invokes bound phase delegates (sync, Task, ValueTask).</summary>
public sealed class DefaultEngine : IEngine
{
    /// <summary>Initializes a new instance of the <see cref="DefaultEngine"/> class.</summary>
    public DefaultEngine()
    {
        // NOOP
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvokeSync(object target, Action<object, SceneContext, CancellationToken>? syndDelegate, SceneContext context, CancellationToken ct)
    {
        syndDelegate!(target, context, ct);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask InvokeValueTaskAsync(object target, Func<object, SceneContext, CancellationToken, ValueTask>? valueTaskDelegate, SceneContext context, CancellationToken ct)
    {
        return valueTaskDelegate!(target, context, ct);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task InvokeTaskAsync(object target, Func<object, SceneContext, CancellationToken, Task>? taskDelegate, SceneContext context, CancellationToken ct)
    {
        return taskDelegate!(target, context, ct);
    }
}
