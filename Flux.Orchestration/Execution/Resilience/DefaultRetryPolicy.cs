namespace Flux.Orchestration.Execution.Resilience;

/// <summary>
/// Retries every failure up to the phase's <c>MaxRetries</c> with a fixed delay, unless an optional
/// <c>isFatal</c> predicate classifies the exception as non-retryable.
/// </summary>
public sealed class DefaultRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _delay;
    private readonly Func<Exception, bool>? _isFatal;

    public DefaultRetryPolicy(TimeSpan delay = default, Func<Exception, bool>? isFatal = null)
    {
        _delay = delay;
        _isFatal = isFatal;
    }

    public bool ShouldRetry(Exception exception, int attempt, int maxRetries)
        => attempt < maxRetries && (_isFatal is null || !_isFatal(exception));

    public TimeSpan GetRetryDelay(int attempt) => _delay;
}
