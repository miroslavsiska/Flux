using System.Collections.Concurrent;

namespace Flux.Orchestration.Durability;

/// <summary>
/// In-memory journal that retains the ordered event stream for inspection and replay. Thread-safe.
/// </summary>
/// <remarks>
/// Suitable for tests, diagnostics, and as the reference implementation that a persistent backend can mirror.
/// Sequence numbers are monotonic and gap-free; <see cref="Read"/> returns events in append order.
/// </remarks>
public sealed class InMemoryOrchestrationJournal : IOrchestrationJournal
{
    private readonly ConcurrentQueue<JournalRecord> _records = new();
    private long _sequence;

    public int Count => _records.Count;

    public ValueTask AppendAsync(JournalEvent journalEvent, CancellationToken cancellationToken = default)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _records.Enqueue(new JournalRecord(seq, DateTimeOffset.UtcNow, journalEvent));
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns the recorded events in append order.</summary>
    public IReadOnlyList<JournalRecord> Read() => _records.OrderBy(r => r.Sequence).ToArray();

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<JournalRecord>> ReadAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Read());
}
