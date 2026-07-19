using Flux.Orchestration.Model;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Flux.Orchestration.Execution.Resilience;

/// <summary>
/// A consecutive-failure circuit breaker with a time-based cooldown and a half-open trial state.
/// </summary>
/// <remarks>
/// Opens once a phase reaches <c>failureThreshold</c> consecutive failures. While open it short-circuits for
/// <c>openDuration</c>; afterwards it enters a half-open state (a single trial is allowed). A trial success
/// closes the circuit; a trial failure reopens it. Timing uses a monotonic <see cref="Stopwatch"/> clock.
/// </remarks>
public sealed class CircuitBreaker : ICircuitBreaker
{
    private sealed class State
    {
        public int Failures;
        public long OpenedAtTicks; // 0 = closed
    }

    private readonly ConcurrentDictionary<OrchestrationKey, State> _states = new();
    private readonly int _threshold;
    private readonly long _openDurationTicks;

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? openDuration = null)
    {
        _threshold = Math.Max(1, failureThreshold);
        var duration = openDuration ?? TimeSpan.FromSeconds(30);
        _openDurationTicks = (long)(duration.TotalSeconds * Stopwatch.Frequency);
    }

    public bool IsOpen(OrchestrationKey key)
    {
        if (!_states.TryGetValue(key, out var s)) return false;
        lock (s)
        {
            if (s.OpenedAtTicks == 0) return false;
            // Cooldown elapsed → half-open: allow a single trial through.
            return Stopwatch.GetTimestamp() - s.OpenedAtTicks < _openDurationTicks;
        }
    }

    public void RecordSuccess(OrchestrationKey key)
    {
        if (_states.TryGetValue(key, out var s))
            lock (s) { s.Failures = 0; s.OpenedAtTicks = 0; }
    }

    public void RecordFailure(OrchestrationKey key)
    {
        var s = _states.GetOrAdd(key, static _ => new State());
        lock (s)
        {
            s.Failures++;
            if (s.Failures >= _threshold)
                s.OpenedAtTicks = Stopwatch.GetTimestamp(); // (re)open
        }
    }
}
