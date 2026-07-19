namespace Flux.Orchestration.Execution.Resilience;

/// <summary>
/// Decides whether a failed phase attempt should be retried and how long to wait before the next attempt.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Returns whether another attempt should be made after the given <paramref name="exception"/>.
    /// </summary>
    /// <param name="exception">The failure (a <see cref="TimeoutException"/> for timeouts).</param>
    /// <param name="attempt">Zero-based index of the attempt that just failed.</param>
    /// <param name="maxRetries">The phase's configured maximum retry count.</param>
    bool ShouldRetry(Exception exception, int attempt, int maxRetries);

    /// <summary>Delay to wait before the attempt following <paramref name="attempt"/>.</summary>
    TimeSpan GetRetryDelay(int attempt);
}
