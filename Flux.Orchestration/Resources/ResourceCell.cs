namespace Flux.Orchestration.Resources;

/// <summary>A value paired with the monotonic version at which it was published.</summary>
/// <typeparam name="T">The resource value type.</typeparam>
/// <param name="Version">Monotonic version; bumped on every write (0 = initial).</param>
/// <param name="Value">The published value.</param>
public readonly record struct Versioned<T>(long Version, T Value);

/// <summary>Lets the store read a cell's version and boxed value without knowing its element type.</summary>
internal interface IVersionedCell
{
    long Version { get; }
    object? BoxedValue { get; }
}

/// <summary>
/// A lock-free, single-writer/multi-reader versioned cell. Readers always observe a complete, self-consistent
/// value (the value and its version are swapped atomically as one immutable holder via <see cref="Volatile"/>),
/// so there are never torn reads — the same torn-read-free guarantee a snapshot double-buffer gives, generalized
/// to arbitrary typed data. Writes are serialized so the version stays consistent with the value.
/// </summary>
/// <typeparam name="T">The resource value type.</typeparam>
public sealed class ResourceCell<T> : IVersionedCell
{
    // Immutable holder: a write swaps in a brand-new instance, so a reader either sees the old (value,version)
    // pair or the new one in full — never a mix.
    private sealed class Holder
    {
        public readonly long Version;
        public readonly T Value;
        public Holder(long version, T value) { Version = version; Value = value; }
    }

    private Holder _holder;
    private readonly object _writeLock = new();

    public ResourceCell(T initial = default!) => _holder = new Holder(0, initial);

    /// <summary>The current value (atomic, race-free read).</summary>
    public T Value => Volatile.Read(ref _holder).Value;

    /// <summary>The current version.</summary>
    public long Version => Volatile.Read(ref _holder).Version;

    /// <summary>The current value boxed as <see cref="object"/> — for type-agnostic snapshotting.</summary>
    object? IVersionedCell.BoxedValue => Volatile.Read(ref _holder).Value;

    /// <summary>Reads the value together with its version as one consistent pair.</summary>
    public Versioned<T> Read()
    {
        var h = Volatile.Read(ref _holder);
        return new Versioned<T>(h.Version, h.Value);
    }

    /// <summary>Publishes a new value, bumping the version. Returns the new version.</summary>
    public long Write(T value)
    {
        lock (_writeLock)
        {
            var next = _holder.Version + 1;
            Volatile.Write(ref _holder, new Holder(next, value));
            return next;
        }
    }
}
