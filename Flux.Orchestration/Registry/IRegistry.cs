using Flux.Orchestration.Model;

namespace Flux.Orchestration.Registry;

/// <summary>Registry for scenes, phases, and component phase targets.</summary>
public interface IRegistry
{
    /// <summary>Adds a scene.</summary>
    /// <param name="scene">The scene to add.</param>
    void  Add(Scene scene);

    /// <summary>Gets the scene with the given ID, or <see langword="null"/> if none.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns>The matching <see cref="Scene"/>, or <see langword="null"/>.</returns>
    Scene? Get(string sceneId);

    /// <summary>Gets the scene phase for the given key, or <see langword="null"/> if none.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <returns>The <see cref="ScenePhase"/>, or <see langword="null"/>.</returns>
    ScenePhase? Get(OrchestrationKey key);

    /// <summary>Gets all registered scenes.</summary>
    /// <returns>A read-only list of scenes; empty if none.</returns>
    IReadOnlyList<Scene> GetAll();

    /// <summary>Determines whether a scene with the given ID is registered.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns><see langword="true"/> if registered; otherwise <see langword="false"/>.</returns>
    bool IsRegistered(string sceneId);

    /// <summary>Determines whether a scene is valid, reporting any phases with no target.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="missingPhases">Receives the phases missing a target when invalid; otherwise empty.</param>
    /// <returns><see langword="true"/> if valid; otherwise <see langword="false"/>.</returns>
    bool IsValid(string sceneId, out List<string> missingPhases);
       
    /// <summary>
    /// Adds a phase target binding an instance (and optional method) to the given key. A <see langword="null"/>
    /// <paramref name="methodName"/> uses the instance's default behavior.
    /// </summary>
    /// <param name="key">The orchestration phase key.</param>
    /// <param name="instance">The target instance.</param>
    /// <param name="methodName">The method to invoke, or <see langword="null"/> for the default.</param>
    void AddPhaseTarget(OrchestrationKey key, object instance, string? methodName);

    /// <summary>Removes the phase target matching the given key and instance. No-op if none matches.</summary>
    /// <param name="key">The orchestration phase key.</param>
    /// <param name="instance">The target instance to remove.</param>
    void RemovePhaseTarget(OrchestrationKey key, object instance);

    /// <summary>Adds a target to the phase identified by the given key.</summary>
    /// <param name="key">The orchestration phase key.</param>
    /// <param name="target">The target to add.</param>
    void AddPhaseTarget(OrchestrationKey key, ScenePhaseTarget target);

    /// <summary>Removes the target from the phase for the given key. No-op if the target is not present.</summary>
    /// <param name="key">The orchestration phase key.</param>
    /// <param name="target">The target to remove.</param>
    void RemovePhaseTarget(OrchestrationKey key, ScenePhaseTarget target);

    /// <summary>Gets the phases of the given scene.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns>A read-only list of phases; empty if none.</returns>
    IReadOnlyList<ScenePhase> GetPhases(string sceneId);


    /// <summary>Gets the phase targets bound to the given key.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <returns>The matching <see cref="ScenePhaseTarget"/> objects; may be empty.</returns>
    IEnumerable<ScenePhaseTarget> GetTargets(OrchestrationKey key);

    ///// <summary>
    ///// Retrieves the target method associated with the specified scene and phase.
    ///// </summary>
    ///// <param name="scene">The scene instance for which the target method is being retrieved. Cannot be <see langword="null"/>.</param>
    ///// <param name="phase">The phase of the scene that determines the target method.</param>
    ///// <returns>A <see cref="MethodBindingInfo"/> object representing the target method, or <see langword="null"/> if no matching method is found.</returns>
    //MethodBindingInfo? GetTargetMethod(Scene scene, ScenePhase phase);

    /// <summary>Determines whether a target is registered for the given key.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <returns><see langword="true"/> if a target is registered; otherwise <see langword="false"/>.</returns>
    bool IsTargetRegistered(OrchestrationKey key);

    /// <summary>
    /// Registers a component, discovering its methods annotated with <see cref="Attributes.SceneMethodAttribute"/>
    /// as phase targets.
    /// </summary>
    /// <param name="component">The component to register.</param>
    void RegisterComponent(object component);

    /// <summary>Unregisters a previously registered component.</summary>
    /// <param name="component">The component to unregister.</param>
    bool? UnregisterComponent(object component);
}
