namespace Flux.Orchestration.Durability;

/// <summary>
/// No-op journal — the default. Its presence lets the planner treat journaling as disabled (zero overhead).
/// </summary>
public sealed class NullOrchestrationJournal : IOrchestrationJournal
{
    public static readonly NullOrchestrationJournal Instance = new();

    public ValueTask AppendAsync(JournalEvent journalEvent, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<JournalRecord>> ReadAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<JournalRecord>>([]);
}
