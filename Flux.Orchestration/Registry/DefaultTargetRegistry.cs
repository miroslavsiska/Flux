using Flux.Orchestration.MethodBinding.Builder;
using Flux.Orchestration.Model;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Flux.Orchestration.Registry;

/// <summary>
/// Default in-memory <see cref="ITargetRegistry"/>: thread-safe (concurrent) storage of phase targets keyed by
/// <see cref="OrchestrationKey"/>, with a reverse instance→keys index for fast unregistration/disposal and immutable
/// per-key snapshots for lock-free resolution.
/// </summary>
public class DefaultTargetRegistry : ITargetRegistry
{
    private readonly ScenePhaseTargetMetadataBuilder _builder;
    private readonly ILogger<DefaultTargetRegistry> _logger;
    private readonly ConcurrentDictionary<OrchestrationKey, HashSet<ScenePhaseTargetMetadata>> _targetRegistry = [];

    // REVERZNÍ INDEX: Instance -> Množina klíčů (pro bleskový disposal)
    private readonly ConcurrentDictionary<object, HashSet<OrchestrationKey>> _instanceToKeys = [];

    // Immutable per-key snapshot, kept in sync with the HashSet under lock(list). Lets ResolveMetadata
    // (the per-tick hot path) return a cached array with zero allocation instead of materializing ToList().
    private readonly ConcurrentDictionary<OrchestrationKey, ScenePhaseTargetMetadata[]> _metadataSnapshots = [];

    // Caller MUST hold lock(list). Rebuilds (or drops) the cached snapshot for a key after a mutation.
    private void RefreshSnapshot(OrchestrationKey key, HashSet<ScenePhaseTargetMetadata> list)
    {
        if (list.Count == 0)
            _metadataSnapshots.TryRemove(key, out _);
        else
            _metadataSnapshots[key] = [.. list];
    }

    public DefaultTargetRegistry(ScenePhaseTargetMetadataBuilder builder, ILogger<DefaultTargetRegistry> logger)
    {
        _builder = builder;
        _logger = logger;
    }

    public void Register(OrchestrationKey key, object instance, string? methodName)
    {
        var metadata = _builder.Build(instance, methodName);
        RegisterCore(key, metadata);
    }

    public void Register(OrchestrationKey key, ScenePhaseTarget target)
    {
        var metadata = _builder.Build(target);
        RegisterCore(key, metadata);
    }

    public void Register(OrchestrationKey key, IReadOnlyList<ScenePhaseTarget> targets)
    {
        if (targets is null || targets.Count == 0)
            return;

        var list = _targetRegistry.GetOrAdd(key, _ => []);

        lock (list) // Ensure thread-safe mutation
        {
            foreach (var target in targets)
            {
                var metadata = _builder.Build(target);
                RegisterCore(key, metadata);
            }
        }
    }

    public void Register(IReadOnlyDictionary<OrchestrationKey, IReadOnlyList<ScenePhaseTarget>> targets)
    {
        if (targets is null || targets.Count == 0)
            return;

        foreach (var (key, targetList) in targets)
        {
            if (targetList == null || targetList.Count == 0)
                continue;

            var list = _targetRegistry.GetOrAdd(key, _ => []);

            lock (list) // Ensure thread-safe mutation
            {
                foreach (var target in targetList)
                {
                    var metadata = _builder.Build(target);
                    RegisterCore(key, metadata);
                }
            }
        }
    }

    private void RegisterCore(OrchestrationKey key, ScenePhaseTargetMetadata metadata)
    {
        // 1. Přidání do hlavního registru
        var list = _targetRegistry.GetOrAdd(key, _ => []);
        lock (list)
        {
            if (!list.Any(m => m.Target.Equals(metadata.Target) && m.MethodBindingInfo?.Equals(metadata.MethodBindingInfo) == true))
            {
                list.Add(metadata);
            }
            RefreshSnapshot(key, list);
        }

        // 2. Aktualizace reverzního indexu
        var keySet = _instanceToKeys.GetOrAdd(metadata.Target.Instance, _ => []);
        lock (keySet)
        {
            keySet.Add(key);
        }
    }

    public bool Unregister(OrchestrationKey key)
    {
        _metadataSnapshots.TryRemove(key, out _);
        return _targetRegistry.TryRemove(key, out _);
    }

    public bool Unregister(object instance)
    {
        if (!_instanceToKeys.TryRemove(instance, out var keysToRemove))
            return false;

        bool anyRemoved = false;
        foreach (var key in keysToRemove)
        {
            if (_targetRegistry.TryGetValue(key, out var list))
            {
                lock (list)
                {
                    anyRemoved |= list.RemoveWhere(m => ReferenceEquals(m.Target.Instance, instance)) > 0;
                    if (list.Count == 0) _targetRegistry.TryRemove(key, out _);
                    RefreshSnapshot(key, list);
                }
            }
        }
        return anyRemoved;
    }

    public bool Unregister(OrchestrationKey key, object instance)
    {
        if (_targetRegistry.TryGetValue(key, out var list))
        {
            bool removed;
            lock (list)
            {
                // 1. Odstraníme z hlavního registru
                removed = list.RemoveWhere(t => ReferenceEquals(t.Instance, instance)) > 0;

                if (list.Count == 0)
                {
                    _targetRegistry.TryRemove(key, out _);
                }
                RefreshSnapshot(key, list);
            }

            if (removed)
            {
                // 2. Synchronizujeme reverzní index — tato instance už v této konkrétní fázi není.
                if (_instanceToKeys.TryGetValue(instance, out var keys))
                {
                    lock (keys)
                    {
                        keys.Remove(key);

                        // Pokud už instance není v žádné fázi, smažeme ji z indexu úplně (ať netečou prázdné HashSety).
                        if (keys.Count == 0)
                        {
                            _instanceToKeys.TryRemove(instance, out _);
                        }
                    }
                }
            }

            return removed;
        }
        return false;
    }

    public bool Unregister(OrchestrationKey key, ScenePhaseTarget target)
    {
        if (_targetRegistry.TryGetValue(key, out var list))
        {
            bool removed;
            lock (list)
            {
                removed = list.RemoveWhere(m => m.Target.Equals(target)) > 0;

                if (list.Count == 0)
                {
                    _targetRegistry.TryRemove(key, out _);
                }
                RefreshSnapshot(key, list);
            }

            if (removed)
            {
                // 2. Synchronizujeme reverzní index — tato instance už v této konkrétní fázi není.
                if (_instanceToKeys.TryGetValue(target.Instance, out var keys))
                {
                    lock (keys)
                    {
                        keys.Remove(key);

                        // Pokud už instance není v žádné fázi, smažeme ji z indexu úplně (ať netečou prázdné HashSety).
                        if (keys.Count == 0)
                        {
                            _instanceToKeys.TryRemove(target.Instance, out _);
                        }
                    }
                }
            }
        }
        return false;
    }

    public ScenePhaseTarget? ResolveFirstOrDefault(OrchestrationKey key)
    {
        return _targetRegistry.TryGetValue(key, out var list) ? list.FirstOrDefault()?.Target : null;
    }

    public IReadOnlyList<ScenePhaseTarget> ResolveWhere(Func<OrchestrationKey, ScenePhaseTarget, bool> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        return [.. _targetRegistry
            .SelectMany(kvp => kvp.Value.ToList()
                .Where(metadata => predicate(kvp.Key, metadata.Target))
                .Select(metadata => metadata.Target))];
    }

    public IReadOnlyList<ScenePhaseTarget> ResolveWhere(Func<ScenePhaseTarget, bool> predicate)
    {
        return [.. _targetRegistry
            .SelectMany(kvp => kvp.Value.ToList()
                .Where(metadata => predicate(metadata.Target))
                .Select(metadata => metadata.Target))];
    }

    public IReadOnlyList<OrchestrationKey> ResolveWhereWithKey(Func<OrchestrationKey, bool> predicate)
    {
        return [.. _targetRegistry.Keys.Where(predicate)];
    }

    public IReadOnlyList<(OrchestrationKey Key, ScenePhaseTarget Target)> ResolvePairsWhere(
        Func<OrchestrationKey, ScenePhaseTarget, bool> predicate)
    {
        return [.. _targetRegistry
            .SelectMany(kvp => kvp.Value.ToList()
                .Where(metadata => predicate(kvp.Key, metadata.Target))
                .Select(metadata => (kvp.Key, metadata.Target)))];
    }

    public IReadOnlyList<ScenePhaseTarget> Resolve(OrchestrationKey key)
    {
        return _targetRegistry.TryGetValue(key, out var list) ? list.ToList().Select(metadata => metadata.Target).ToList() : [];
    }

    public IReadOnlyList<ScenePhaseTargetMetadata> ResolveMetadata(OrchestrationKey key)
    {
        // Hot path (per phase, per tick): return the cached immutable snapshot — no per-call allocation.
        // The snapshot is kept in sync with the underlying HashSet under lock at every mutation site.
        return _metadataSnapshots.TryGetValue(key, out var snapshot) ? snapshot : [];
    }

    public IReadOnlyList<OrchestrationKey> Resolve(object instance)
    {
        if (_instanceToKeys.TryGetValue(instance, out var keys))
        {
            lock (keys)
            {
                return keys.ToList();
            }
        }
        return [];
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ScenePhaseTarget>> ResolveAll(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("Scene ID cannot be null or whitespace.", nameof(sceneId));

        return _targetRegistry
            .Where(kvp => kvp.Key.SceneId == sceneId)
            .GroupBy(kvp => kvp.Key.PhaseId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ScenePhaseTarget>)[.. g
                    .SelectMany(kvp => kvp.Value)
                    .Select(metadata => metadata.Target)]
            );
    }
}
