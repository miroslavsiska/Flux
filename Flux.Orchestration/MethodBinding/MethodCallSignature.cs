namespace Flux.Orchestration.MethodBinding;

/// <summary>The signature style of a bound method: synchronous, Task-based, or ValueTask-based.</summary>
public enum MethodCallSignature
{
    /// <summary>Synchronous, void-returning method.</summary>
    Sync,

    /// <summary>Asynchronous method returning a <see cref="System.Threading.Tasks.Task"/>.</summary>
    Task,

    /// <summary>Asynchronous method returning a <see cref="System.Threading.Tasks.ValueTask"/>.</summary>
    ValueTask
}