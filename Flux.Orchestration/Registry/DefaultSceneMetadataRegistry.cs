using Flux.Orchestration.Model;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Flux.Orchestration.Registry;

/// <inheritdoc cref="ISceneMetadataRegistry" />
public class DefaultSceneMetadataRegistry : ISceneMetadataRegistry
{
    private readonly ILogger<DefaultSceneMetadataRegistry> _logger;
    private readonly ConcurrentDictionary<string, SceneMetadata> _registry = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSceneMetadataRegistry"/> class.
    /// </summary>
    /// <param name="logger">The logger instance used to log messages related to the operations of the registry.</param>
    public DefaultSceneMetadataRegistry(ILogger<DefaultSceneMetadataRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Register(SceneMetadata sceneMetadata)
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Registering scene metadata with ID '{sceneMetadata.Id}' with {sceneMetadata.Phases?.Count} phases.");
        _registry[sceneMetadata.Id] = sceneMetadata;
    }

    /// <inheritdoc />
    public bool IsRegistered(string sceneId)
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Checking if scene metadata with ID '{sceneId}' is registered.");
        return _registry.ContainsKey(sceneId);
    }

    /// <inheritdoc />
    public SceneMetadata? Resolve(string sceneId)
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Resolving scene metadata with ID '{sceneId}'.");
        if (_registry.TryGetValue(sceneId, out var sceneMetadata))
        {
            _logger.LogTrace($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' found.");
            return sceneMetadata;
        }
        _logger.LogWarning($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' not found.");
        return null;
    }

    /// <inheritdoc />
    public ScenePhaseMetadata? Resolve(OrchestrationKey key)
    {
        var sceneId = key.SceneId;
        var phaseId = key.PhaseId;

        _logger.LogTrace($"[SceneMetadataRegistry] Resolving scene phase metadata with scene ID '{sceneId}' phase ID '{phaseId}'.");
        if (_registry.TryGetValue(sceneId, out var sceneMetadata))
        {
            _logger.LogTrace($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' found.");
            var phase = sceneMetadata.GetPhase(phaseId);
            if(phase is not null)
            {
                _logger.LogTrace($"[SceneMetadataRegistry] Phase with ID '{phaseId}' found in scene metadata with ID '{sceneId}'.");
                return phase;
            }
            _logger.LogWarning($"[SceneMetadataRegistry] Scene phase metadata with ID '{phaseId}' not found in scene metadata with ID '{sceneId}'.");
            return null;
        }
        _logger.LogWarning($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' not found.");
        return null;
    }

    /// <inheritdoc />
    public IEnumerable<SceneMetadata> ResolveAll()
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Resolving all scene metadata. Total count: {_registry.Count}.");
        return _registry.Values;       
    }

    /// <inheritdoc />
    public IEnumerable<SceneMetadata> ResolveBySignal(string signal)
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Resolving scenes metadata for signal '{signal}'.");
        return _registry.Values.Where(s => s.Triggers.Any(t => t.Signal == signal));
    }


    /// <inheritdoc />
    public bool Unregister(string sceneId)
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Unregistering scene metadata with ID '{sceneId}'.");
        return _registry.Remove(sceneId, out var _);
    }

    /// <inheritdoc />
    public bool Unregister(OrchestrationKey key)
    {
        var sceneId = key.SceneId;
        var phaseId = key.PhaseId;
        _logger.LogTrace($"[SceneMetadataRegistry] Unregistering phase with ID '{phaseId}' from scene metadata with ID '{sceneId}'.");
        if (!_registry.TryGetValue(sceneId, out var sceneMetadata))
        {
            _logger.LogWarning($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' not found.");
            return false;
        }
        _logger.LogTrace($"[SceneMetadataRegistry] Removing phase with ID '{phaseId}' from scene metadata with ID '{sceneId}'.");
        return sceneMetadata.RemovePhase(phaseId);
    }

    /// <inheritdoc />
    public bool Unregister(string sceneId, ScenePhaseMetadata phase)
    {
        _logger.LogTrace($"[SceneMetadataRegistry] Unregistering phase '{phase.PhaseId}' from scene metadata with ID '{sceneId}'.");
        if (!_registry.TryGetValue(sceneId, out var sceneMetadata))
        {
            _logger.LogWarning($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' not found.");
            return false;
        }
        _logger.LogTrace($"[SceneMetadataRegistry] Removing phase '{phase.PhaseId}' from scene metadata with ID '{sceneId}'.");
        return sceneMetadata.RemovePhase(phase);
    }

    /// <inheritdoc />
    public bool Unregister(OrchestrationKey key, string signalName)
    {
        var sceneId = key.SceneId;
        var phaseId = key.PhaseId;
        _logger.LogTrace($"[SceneMetadataRegistry] Unregistering signal '{signalName}' from phase '{phaseId}' in scene metadata with ID '{sceneId}'.");
        if (!_registry.TryGetValue(sceneId, out var sceneMetadata))
        {
            _logger.LogWarning($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' not found.");
            return false;
        }
        _logger.LogTrace($"[SceneMetadataRegistry] Removing signal '{signalName}' from phase '{phaseId}' in scene metadata with ID '{sceneId}'.");
        return sceneMetadata.RemoveSignal(signalName);
    }

    /// <inheritdoc />
    public bool Unregister(OrchestrationKey key, SignalBinding signalBinding)
    {
        var sceneId = key.SceneId;
        var phaseId = key.PhaseId;
        _logger.LogTrace($"[SceneMetadataRegistry] Unregistering signal binding from phase '{phaseId}' in scene metadata with ID '{sceneId}'.");
        if (!_registry.TryGetValue(sceneId, out var sceneMetadata))
        {
            _logger.LogWarning($"[SceneMetadataRegistry] Scene metadata with ID '{sceneId}' not found.");
            return false;
        }
        _logger.LogTrace($"[SceneMetadataRegistry] Removing signal binding from phase '{phaseId}' in scene metadata with ID '{sceneId}'.");
        return sceneMetadata.RemoveSignal(signalBinding!);
    }
}
