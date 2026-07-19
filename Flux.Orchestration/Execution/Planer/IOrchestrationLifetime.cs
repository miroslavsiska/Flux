namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// Controls the runtime lifecycle of the orchestration loop and exposes the manual tick entry-point.
/// </summary>
/// <remarks>
/// Consumers that obtain <see cref="IPlanner"/> through DI should also resolve
/// <see cref="IOrchestrationLifetime"/> to start and stop the internal processing loop.
/// Separating lifecycle from planning concerns keeps <see cref="IPlanner"/> focused
/// purely on scene registration and signal dispatch.
/// </remarks>
public interface IOrchestrationLifetime : IAsyncDisposable
{
    /// <summary>
    /// Starts the internal orchestration loop on a background thread.
    /// Calling <see cref="Start"/> more than once is a no-op.
    /// </summary>
    void Start();

    /// <summary>
    /// Signals the loop to stop and waits asynchronously for it to drain.
    /// </summary>
    ValueTask StopAsync();

    /// <summary>
    /// Advances all active scenes by one step with the given <paramref name="delta"/> time.
    /// Can be driven externally (e.g. from a game loop) instead of the internal loop.
    /// </summary>
    /// <param name="delta">Elapsed time since the last tick.</param>
    /// <param name="cancellationToken">Token to observe during tick processing.</param>
    void Tick(TimeSpan delta, CancellationToken cancellationToken = default);
}
