using System.Text.Json;

namespace Flux.Orchestration.Durability;

/// <summary>
/// Append-only, file-backed journal: one JSON record per line (JSON Lines). Appends are durable and crash-safe
/// — each line is a complete record, so a torn final write at most loses the last (un-acked) event. This is the
/// persistent event source a restart replays. Sequence numbers are monotonic and gap-free.
/// </summary>
public sealed class FileOrchestrationJournal : IOrchestrationJournal
{
    // On-disk line shape. JournalEvent/JournalRecord are records and serialize directly.
    private sealed record Line(long Sequence, DateTimeOffset Timestamp, JournalEvent Event);

    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = false };
    private readonly Lock _ioLock = new();
    private long _sequence;

    public FileOrchestrationJournal(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Resume the sequence counter past any existing records so appends stay monotonic across restarts.
        if (File.Exists(_path))
        {
            long max = 0;
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var rec = JsonSerializer.Deserialize<Line>(line, _options);
                if (rec is not null && rec.Sequence > max) max = rec.Sequence;
            }
            _sequence = max;
        }
    }

    public ValueTask AppendAsync(JournalEvent journalEvent, CancellationToken cancellationToken = default)
    {
        lock (_ioLock)
        {
            var seq = ++_sequence;
            var line = JsonSerializer.Serialize(new Line(seq, DateTimeOffset.UtcNow, journalEvent), _options);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<JournalRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<JournalRecord>();
        if (File.Exists(_path))
        {
            foreach (var line in File.ReadLines(_path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var rec = JsonSerializer.Deserialize<Line>(line, _options);
                if (rec is not null)
                    records.Add(new JournalRecord(rec.Sequence, rec.Timestamp, rec.Event));
            }
        }
        records.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        return ValueTask.FromResult<IReadOnlyList<JournalRecord>>(records);
    }
}
