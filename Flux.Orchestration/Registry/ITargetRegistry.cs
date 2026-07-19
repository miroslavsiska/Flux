using Flux.Orchestration.Model;

namespace Flux.Orchestration.Registry;

/// <summary>
/// Maps each <see cref="OrchestrationKey"/> (scene + phase) to the targets bound to it, with register/unregister and
/// rich resolve/query operations. Backs the agent's phase-target lookup at execution time.
/// </summary>
public interface ITargetRegistry
{
    void Register(OrchestrationKey key, object instance, string? methodName);

    void Register(OrchestrationKey key, ScenePhaseTarget instance);

    void Register(OrchestrationKey key, IReadOnlyList<ScenePhaseTarget> targets);

    void Register(IReadOnlyDictionary<OrchestrationKey, IReadOnlyList<ScenePhaseTarget>> targets);

    bool Unregister(OrchestrationKey key);

    bool Unregister(object instance);

    bool Unregister(OrchestrationKey key, object instance);

    bool Unregister(OrchestrationKey key, ScenePhaseTarget target);

    ScenePhaseTarget? ResolveFirstOrDefault(OrchestrationKey key);

    IReadOnlyList<ScenePhaseTarget> ResolveWhere(Func<OrchestrationKey, ScenePhaseTarget, bool> predicate);

    IReadOnlyList<ScenePhaseTarget> ResolveWhere(Func<ScenePhaseTarget, bool> predicate);

    IReadOnlyList<OrchestrationKey> ResolveWhereWithKey(Func<OrchestrationKey, bool> predicate);

    IReadOnlyList<(OrchestrationKey Key, ScenePhaseTarget Target)> ResolvePairsWhere(Func<OrchestrationKey, ScenePhaseTarget, bool> predicate);

    IReadOnlyList<ScenePhaseTarget> Resolve(OrchestrationKey key);

    IReadOnlyList<ScenePhaseTargetMetadata> ResolveMetadata(OrchestrationKey key);

    IReadOnlyList<OrchestrationKey> Resolve(object instance);

    IReadOnlyDictionary<string, IReadOnlyList<ScenePhaseTarget>> ResolveAll(string sceneId);
}
