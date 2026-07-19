namespace Flux.Orchestration.Execution.Resilience;

/// <summary>Why a phase was dead-lettered.</summary>
public enum DeadLetterReason
{
    /// <summary>The phase failed and exhausted all retry attempts.</summary>
    RetriesExhausted,

    /// <summary>The phase was short-circuited by an open circuit breaker.</summary>
    CircuitOpen,
}

/// <summary>
/// Describes a phase invocation that ultimately failed, handed to an <see cref="IDeadLetterSink"/> so the
/// failure can be captured (and potentially replayed) instead of aborting the surrounding scene.
/// </summary>
/// <param name="OrchestrationId">Scene id.</param>
/// <param name="PhaseId">Phase id.</param>
/// <param name="Target">The target instance whose invocation failed.</param>
/// <param name="Attempts">Number of attempts made (0 when short-circuited).</param>
/// <param name="Exception">The final failure.</param>
/// <param name="Reason">Why it was dead-lettered.</param>
public sealed record DeadLetterContext(
    string OrchestrationId,
    string PhaseId,
    object Target,
    int Attempts,
    Exception Exception,
    DeadLetterReason Reason);

/// <summary>
/// Receives phases that ultimately failed. Configuring a sink switches the scheduler from "throw on final
/// failure" to "capture and continue", so one failing phase no longer aborts its DAG level/scene.
/// </summary>
public interface IDeadLetterSink
{
    ValueTask HandleAsync(DeadLetterContext context, CancellationToken cancellationToken);
}
