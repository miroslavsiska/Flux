using Flux.Orchestration.Model;

namespace Flux.Orchestration.Execution.Resilience;

/// <summary>
/// Per-phase circuit breaker. After a phase fails repeatedly the breaker "opens" and the scheduler
/// fast-fails further invocations (without calling the target) until a cooldown elapses — preventing a
/// persistently broken target from consuming resources every tick.
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>Whether the circuit for <paramref name="key"/> is currently open (calls should be short-circuited).</summary>
    bool IsOpen(OrchestrationKey key);

    /// <summary>Records a successful execution, closing the circuit.</summary>
    void RecordSuccess(OrchestrationKey key);

    /// <summary>Records a failed execution, possibly opening the circuit.</summary>
    void RecordFailure(OrchestrationKey key);
}
