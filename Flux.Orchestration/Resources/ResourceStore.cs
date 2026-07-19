using System.Collections.Concurrent;

namespace Flux.Orchestration.Resources;

/// <summary>
/// Default <see cref="IResourceStore"/>: a concurrent dictionary of typed <see cref="ResourceCell{T}"/>s.
/// Keys are case-insensitive (matching <c>SceneContext.Parameters</c>). Reading or writing a name with a type
/// that does not match the cell's element type throws — the store is strongly typed per name.
/// </summary>
public sealed class ResourceStore : IResourceStore
{
    private readonly ConcurrentDictionary<string, object> _cells = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional hook invoked after every successful write — used to journal resource writes for replay.</summary>
    public Action<string, long, object?>? OnWrite { get; set; }

    public T Read<T>(string name)
    {
        if (TryRead<T>(name, out var value))
            return value;
        throw new KeyNotFoundException($"Resource '{name}' has not been written.");
    }

    public bool TryRead<T>(string name, out T value)
    {
        if (_cells.TryGetValue(name, out var boxed))
        {
            var cell = AsCell<T>(name, boxed);
            value = cell.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public long Write<T>(string name, T value)
    {
        // GetOrAdd a typed cell; an existing cell of a different element type is a programming error.
        var boxed = _cells.GetOrAdd(name, static _ => new ResourceCell<T>());
        var cell = AsCell<T>(name, boxed);
        var version = cell.Write(value);
        OnWrite?.Invoke(name, version, value);
        return version;
    }

    public long WriteBoxed(string name, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Create/locate a cell typed to the value's runtime type so a later typed Write<T> matches it.
        var write = typeof(ResourceStore).GetMethod(nameof(Write))!.MakeGenericMethod(value.GetType());
        return (long)write.Invoke(this, [name, value])!;
    }

    public IReadOnlyDictionary<string, object> Snapshot()
    {
        var map = new Dictionary<string, object>(_cells.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, boxed) in _cells)
            if (boxed is IVersionedCell cell && cell.BoxedValue is { } value)
                map[name] = value;
        return map;
    }

    public long VersionOf(string name)
        => _cells.TryGetValue(name, out var boxed) && boxed is IVersionedCell v ? v.Version : 0;

    public bool Contains(string name) => _cells.ContainsKey(name);

    public IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)_cells.Keys;

    private static ResourceCell<T> AsCell<T>(string name, object boxed)
    {
        if (boxed is ResourceCell<T> typed)
            return typed;
        throw new InvalidOperationException(
            $"Resource '{name}' is of type '{boxed.GetType().GetGenericArguments()[0].Name}', not '{typeof(T).Name}'.");
    }
}
