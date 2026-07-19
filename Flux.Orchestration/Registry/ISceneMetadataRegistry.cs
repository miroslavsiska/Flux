using Flux.Orchestration.Model;

namespace Flux.Orchestration.Registry;

/// <summary>Registers, resolves, and unregisters scene metadata (by scene, phase, or signal binding).</summary>
public interface ISceneMetadataRegistry
{
    /// <summary>Registers scene metadata.</summary>
    /// <param name="sceneMetadata">The scene metadata to register.</param>
    void Register(SceneMetadata sceneMetadata);

    /// <summary>Determines whether a scene with the given ID is registered.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns><see langword="true"/> if registered; otherwise <see langword="false"/>.</returns>
    bool IsRegistered(string sceneId);

    /// <summary>Resolves scene metadata by scene ID, or <see langword="null"/> if unknown.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns>The <see cref="SceneMetadata"/>, or <see langword="null"/>.</returns>
    SceneMetadata? Resolve(string sceneId);

    /// <summary>Resolves phase metadata for the given key, or <see langword="null"/> if none.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <returns>The <see cref="ScenePhaseMetadata"/>, or <see langword="null"/>.</returns>
    ScenePhaseMetadata? Resolve(OrchestrationKey key);

    /// <summary>Resolves all registered scene metadata.</summary>
    /// <returns>All <see cref="SceneMetadata"/>; empty if none.</returns>
    IEnumerable<SceneMetadata> ResolveAll();

    /// <summary>Resolves scenes triggered by the given signal.</summary>
    /// <param name="signal">The signal name.</param>
    /// <returns>The matching <see cref="SceneMetadata"/>; empty if none.</returns>
    IEnumerable<SceneMetadata> ResolveBySignal(string signal);

    /// <summary>Unregisters the scene with the given ID.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns><see langword="true"/> if unregistered; otherwise <see langword="false"/>.</returns>
    bool Unregister(string sceneId);

    /// <summary>Unregisters the phase identified by the given key.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <returns><see langword="true"/> if unregistered; <see langword="false"/> if not found.</returns>
    bool Unregister(OrchestrationKey key);

    /// <summary>Unregisters a phase from the given scene.</summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="phase">The phase to unregister.</param>
    /// <returns><see langword="true"/> if unregistered; otherwise <see langword="false"/>.</returns>
    bool Unregister(string sceneId, ScenePhaseMetadata phase);

    /// <summary>Unregisters a signal by name for the given key.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <param name="signalName">The signal name.</param>
    /// <returns><see langword="true"/> if unregistered; <see langword="false"/> if not found.</returns>
    bool Unregister(OrchestrationKey key, string signalName);

    /// <summary>Unregisters a signal binding for the given key. No change if the binding is absent.</summary>
    /// <param name="key">The orchestration key.</param>
    /// <param name="signalBinding">The signal binding to remove.</param>
    /// <returns><see langword="true"/> if unregistered; otherwise <see langword="false"/>.</returns>
    bool Unregister(OrchestrationKey key, SignalBinding signalBinding);
}
