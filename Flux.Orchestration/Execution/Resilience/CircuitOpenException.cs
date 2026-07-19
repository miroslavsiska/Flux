namespace Flux.Orchestration.Execution.Resilience;

/// <summary>Thrown (when no dead-letter sink is configured) to indicate a phase was short-circuited by an open circuit breaker.</summary>
public sealed class CircuitOpenException : Exception
{
    public CircuitOpenException(string message) : base(message) { }
}
