using Flux.Orchestration.Model;

namespace Flux.Orchestration;

/// <summary>
/// Registers components, phases, and scenes, then executes scenes, phases, and signals in a scene-based workflow.
/// </summary>
public interface IAgent
{
    /// <summary>Registers a scene.</summary>
    /// <param name="scene">The scene to register.</param>
    void RegisterScene(Scene scene);

    /// <summary>Registers a target object for a specific phase within a scene.</summary>
    void RegisterPhaseTarget(string sceneId, string phaseId, object target, string? phaseOrMethodName);

    /// <summary>Registers an instance as a target for an orchestration phase or method.</summary>
    /// <remarks>A <see langword="null"/> <paramref name="phaseOrMethodName"/> registers a default target for the key.</remarks>
    /// <param name="key">Key identifying the orchestration context.</param>
    /// <param name="instance">The instance to associate with the phase or method.</param>
    /// <param name="phaseOrMethodName">The phase or method name, or <see langword="null"/> for a default registration.</param>
    void RegisterPhaseTarget(OrchestrationKey key, object instance, string? phaseOrMethodName);

    /// <summary>Registers a component and auto-discovers orchestration methods via attributes.</summary>
    void RegisterComponent(object component);

    /// <summary>Checks whether a phase has a registered target.</summary>
    bool IsPhaseRegistered(string sceneId, string phaseId);

    /// <summary>Checks whether a phase has a registered target.</summary>
    bool IsPhaseRegistered(OrchestrationKey key);

    /// <summary>Checks whether a scene is registered.</summary>
    bool IsSceneRegistered(string sceneId);

    /// <summary>Executes all phases of a scene in declared order.</summary>
    Task ExecuteSceneAsync(string sceneId, SceneContext context, CancellationToken cancellationToken = default);

    /// <summary>Executes all phases of a scene in declared order, registering it first if needed.</summary>
    Task ExecuteSceneAsync(Scene scene, SceneContext context, CancellationToken cancellationToken = default);

    ///// <summary>
    ///// Executes a specific phase within a scene.
    ///// </summary>
    //Task ExecuteOrchestrationPhaseAsync(string orchestrationId, string phaseId, OrchestrationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues every scene bound to <paramref name="signal"/> for planning (fire-and-forget): the resolved scenes are
    /// handed to the Tick loop and this returns immediately, before they run. To raise a signal and await its reaction,
    /// use <see cref="ExecuteSignalAsync"/>.
    /// </summary>
    Task PlanSignalAsync(string signal, SceneContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a signal and runs its reaction SYNCHRONOUSLY to completion: every scene bound to the signal executes,
    /// highest-priority-first, and the returned task completes only when all their phases have run. Unlike
    /// <see cref="PlanSignalAsync"/> (fire-and-forget — the resolved scenes are queued for the Tick loop), this lets a
    /// caller raise a signal and await the outcome, and lets priority arbitrate when several scenes answer it.
    /// </summary>
    Task ExecuteSignalAsync(string signal, SceneContext context, CancellationToken cancellationToken = default);
}
