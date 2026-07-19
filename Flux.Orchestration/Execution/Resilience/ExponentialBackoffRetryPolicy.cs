namespace Flux.Orchestration.Execution.Resilience;

/// <summary>
/// Retry policy with exponential backoff and optional jitter: delay = base · factor^attempt, capped at a max.
/// Jitter spreads retries to avoid thundering-herd retry storms.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _factor;
    private readonly bool _jitter;
    private readonly Func<Exception, bool>? _isFatal;

    public ExponentialBackoffRetryPolicy(
        TimeSpan baseDelay,
        TimeSpan? maxDelay = null,
        double factor = 2.0,
        bool jitter = true,
        Func<Exception, bool>? isFatal = null)
    {
        _baseDelay = baseDelay;
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _factor = factor <= 1.0 ? 2.0 : factor;
        _jitter = jitter;
        _isFatal = isFatal;
    }

    public bool ShouldRetry(Exception exception, int attempt, int maxRetries)
        => attempt < maxRetries && (_isFatal is null || !_isFatal(exception));

    public TimeSpan GetRetryDelay(int attempt)
    {
        double ms = _baseDelay.TotalMilliseconds * Math.Pow(_factor, attempt);
        ms = Math.Min(ms, _maxDelay.TotalMilliseconds);
        if (_jitter)
            ms *= 0.5 + Random.Shared.NextDouble() * 0.5; // 50–100% of the computed delay
        return TimeSpan.FromMilliseconds(ms);
    }
}
