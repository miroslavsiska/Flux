namespace Flux.Orchestration.Durability;

/// <summary>The kind of orchestration event recorded in the journal.</summary>
public enum OrchestrationEventKind
{
    /// <summary>A signal was received and dispatched to one or more scenes. An <em>input</em> (replay re-applies it).</summary>
    SignalReceived,

    /// <summary>An explicit scene plan was requested via PlanSceneAsync. An <em>input</em> (replay re-applies it).</summary>
    ScenePlanRequested,

    /// <summary>A tick advanced the logical clock by <c>TickDeltaSeconds</c>. Drives deterministic timing replay.</summary>
    Tick,

    /// <summary>A scene's plan was built and dispatched. A <em>decision</em> (replay reproduces it for verification).</summary>
    ScenePlanned,

    /// <summary>A scene tick was skipped because the previous plan was still running (overload).</summary>
    SceneSkippedBusy,

    /// <summary>
    /// A resource was written to a scene's <c>Resources</c> store. Carries the serialized value, so a replay can
    /// reconstruct resource state purely from the log — without re-running the side-effecting target.
    /// </summary>
    ResourceWritten,
}

/// <summary>
/// An append event describing an orchestrator input or decision. The journal stamps it with a sequence number
/// and timestamp on append. Inputs (<see cref="OrchestrationEventKind.SignalReceived"/>,
/// <see cref="OrchestrationEventKind.ScenePlanRequested"/>, <see cref="OrchestrationEventKind.Tick"/>) carry
/// everything a replay needs to re-apply them deterministically.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="SceneId">The scene involved.</param>
/// <param name="PhaseId">The phase involved, when applicable.</param>
/// <param name="CorrelationId">The <see cref="SceneContext.CorrelationId"/> tying related events together.</param>
/// <param name="Detail">Optional free-form detail (e.g. signal name).</param>
/// <param name="Payload">Serialized input parameters (e.g. the signal's context), for replay. Null when not applicable.</param>
/// <param name="IdempotencyKey">Optional caller-supplied key; duplicate keys are applied at most once.</param>
/// <param name="LogicalTimeSeconds">The virtual-clock time at which the event occurred.</param>
/// <param name="TickDeltaSeconds">For <see cref="OrchestrationEventKind.Tick"/>: the elapsed time the tick advanced.</param>
/// <param name="Version">For <see cref="OrchestrationEventKind.ResourceWritten"/>: the resource cell version after the write.</param>
public readonly record struct JournalEvent(
    OrchestrationEventKind Kind,
    string SceneId,
    string? PhaseId,
    Guid CorrelationId,
    string? Detail,
    string? Payload = null,
    string? IdempotencyKey = null,
    double LogicalTimeSeconds = 0,
    double TickDeltaSeconds = 0,
    long Version = 0);

/// <summary>A persisted journal event with its assigned ordering metadata.</summary>
/// <param name="Sequence">Monotonic, gap-free sequence number assigned on append.</param>
/// <param name="Timestamp">When the event was appended.</param>
/// <param name="Event">The event payload.</param>
public sealed record JournalRecord(long Sequence, DateTimeOffset Timestamp, JournalEvent Event);

/// <summary>
/// Append-only journal of orchestration events — the foundation for audit and (future) replay.
/// </summary>
/// <remarks>
/// This records the orchestrator's <em>inputs and decisions</em> (signals received, scenes planned/skipped)
/// in order, which is what a replay engine would re-apply to reconstruct state. The default wiring uses a
/// no-op journal; register your own <see cref="IOrchestrationJournal"/> to enable it. Full durable, crash-
/// resumable persistence (a storage backend + deterministic replay) builds on this abstraction and is a
/// separate layer.
/// </remarks>
public interface IOrchestrationJournal
{
    /// <summary>Appends an event to the journal.</summary>
    ValueTask AppendAsync(JournalEvent journalEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the full event stream in append (sequence) order — the source a replay re-applies. Implementations
    /// that do not retain events (e.g. the no-op journal) return an empty list.
    /// </summary>
    ValueTask<IReadOnlyList<JournalRecord>> ReadAsync(CancellationToken cancellationToken = default);
}
