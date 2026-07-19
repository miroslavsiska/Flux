using Flux.Orchestration.Model;
using System.Text.Json;

namespace Flux.Orchestration.Durability;

/// <summary>
/// File-backed scene state store: one JSON file per scene in a directory, written atomically (temp + move),
/// so state survives a process crash/restart. Parameter values are serialized via an <see cref="IStateSerializer"/>.
/// </summary>
public sealed class FileSceneStateStore : ISceneStateStore
{
    // Persisted shape on disk: the parameter/resource bags are pre-serialized to string payloads by the
    // IStateSerializer. ResourcesPayload defaults to null for snapshots written before resources were captured.
    private sealed record Persisted(
        string SceneId,
        ScenePlanningMode Mode,
        bool PendingInvalidation,
        Guid CorrelationId,
        string ParametersPayload,
        long Version,
        string? ResourcesPayload = null);

    private readonly string _directory;
    private readonly IStateSerializer _serializer;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = false };
    private readonly Lock _ioLock = new();

    public FileSceneStateStore(string directory, IStateSerializer serializer)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        Directory.CreateDirectory(_directory);
    }

    public ValueTask SaveAsync(SceneStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var persisted = new Persisted(
            snapshot.SceneId,
            snapshot.Mode,
            snapshot.PendingInvalidation,
            snapshot.CorrelationId,
            _serializer.Serialize(snapshot.Parameters),
            snapshot.Version,
            snapshot.Resources is { Count: > 0 } res ? _serializer.Serialize(res) : null);

        var json = JsonSerializer.Serialize(persisted, _options);
        var path = PathFor(snapshot.SceneId);
        var temp = path + ".tmp";

        lock (_ioLock)
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true); // atomic replace
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<SceneStateSnapshot>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<SceneStateSnapshot>();
        if (!Directory.Exists(_directory))
            return ValueTask.FromResult<IReadOnlyList<SceneStateSnapshot>>(results);

        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = File.ReadAllText(file);
            var persisted = JsonSerializer.Deserialize<Persisted>(json, _options);
            if (persisted is null) continue;

            results.Add(new SceneStateSnapshot(
                persisted.SceneId,
                persisted.Mode,
                persisted.PendingInvalidation,
                persisted.CorrelationId,
                _serializer.Deserialize(persisted.ParametersPayload),
                persisted.Version,
                persisted.ResourcesPayload is { } rp ? _serializer.Deserialize(rp) : null));
        }
        return ValueTask.FromResult<IReadOnlyList<SceneStateSnapshot>>(results);
    }

    public ValueTask RemoveAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(sceneId);
        lock (_ioLock)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        return ValueTask.CompletedTask;
    }

    private string PathFor(string sceneId)
    {
        // Sanitize the id into a stable, filesystem-safe file name.
        Span<char> buffer = stackalloc char[sceneId.Length];
        var invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < sceneId.Length; i++)
            buffer[i] = Array.IndexOf(invalid, sceneId[i]) >= 0 ? '_' : sceneId[i];
        return Path.Combine(_directory, new string(buffer) + ".json");
    }
}
