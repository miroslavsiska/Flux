using Flux.Orchestration.Attributes;
using Flux.Orchestration.Exceptions;
using Flux.Orchestration.Model;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Flux.Orchestration.Registry;

/// <summary>
/// Default <see cref="IRegistry"/>: composes the metadata and target registries to register, resolve, and
/// validate scenes, phases, and their targets.
/// </summary>
public class DefaultRegistry : IRegistry
{
    private readonly IMetadataFactory _metadataFactory;
    private readonly ISceneMetadataRegistry _metadataRegistry;
    private readonly ITargetRegistry _targetRegistry;
    private readonly ILogger<DefaultRegistry> _logger;

    /// <summary>Initializes a new <see cref="DefaultRegistry"/>.</summary>
    /// <param name="metadataFactory">Builds scene metadata from types.</param>
    /// <param name="sceneMetadataRegistry">Stores scene/phase metadata.</param>
    /// <param name="objectRegistry">Stores phase targets.</param>
    /// <param name="logger">Diagnostics logger.</param>
    public DefaultRegistry(IMetadataFactory metadataFactory, ISceneMetadataRegistry sceneMetadataRegistry, ITargetRegistry objectRegistry, ILogger<DefaultRegistry> logger)
    {
        _metadataFactory = metadataFactory;
        _metadataRegistry = sceneMetadataRegistry;
        _targetRegistry = objectRegistry;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Add(Scene scene)
    {
        // scene metadata
        var sceneMetadata = scene.ToMetadata();
        _metadataRegistry.Register(sceneMetadata);

        // targets
        var targets = scene.GetAllTargets();
        _targetRegistry.Register(targets);
    }


    /// <inheritdoc />
    public Scene? Get(string sceneId)
    {
        SceneMetadata? metadata = _metadataRegistry.Resolve(sceneId);
        IReadOnlyDictionary<string, IReadOnlyList<ScenePhaseTarget>> targets = _targetRegistry.ResolveAll(sceneId);
        return metadata?.ToScene(targets);
    }

    /// <inheritdoc />
    public ScenePhase? Get(OrchestrationKey key)
    {
        ScenePhaseMetadata? metadata = _metadataRegistry.Resolve(key);
        if (metadata is null) throw new OrchestrationKeyNotFoundException(key);

        IReadOnlyList<ScenePhaseTarget> targets = _targetRegistry.Resolve(key);
        return metadata.ToScenePhase(targets ?? []);
    }

    /// <inheritdoc />
    public IReadOnlyList<Scene> GetAll()
    {
        var scenes = new List<Scene>();
        var metadata = _metadataRegistry.ResolveAll();
        foreach (var metadataScene in metadata)
        {
            var targets = _targetRegistry.ResolveAll(metadataScene.Id);
            var scene = metadataScene.ToScene(targets);
            scenes.Add(scene);
        }
        return scenes;
    }

    /// <inheritdoc />
    public bool IsRegistered(string sceneId)
        => _metadataRegistry.IsRegistered(sceneId);

    /// <inheritdoc />
    public bool IsValid(string sceneId, out List<string> missingTargets)
    {
        missingTargets = [];
        var scene = Get(sceneId);
        if (scene is null || scene.Phases.Count == 0) return false;

        foreach (var phase in scene.Phases)
        {
            if (phase.Targets.Count == 0)
            {
                missingTargets.Add(phase.PhaseId);
            }
        }

        return missingTargets.Count == 0;
    }

    /// <inheritdoc />
    public void AddPhaseTarget(OrchestrationKey key, object instance, string? methodName)
    {
        if(_metadataRegistry.Resolve(key) is null)
        {
            throw new OrchestrationKeyNotFoundException(key);
        }        _targetRegistry.Register(key, instance, methodName);
    }

    /// <inheritdoc />
    public void RemovePhaseTarget(OrchestrationKey key, object instance)
    {
        if (_metadataRegistry.Resolve(key) is null)
        {
            throw new OrchestrationKeyNotFoundException(key);
        }        _targetRegistry.Unregister(key, instance);
    }

    /// <inheritdoc />
    public void AddPhaseTarget(OrchestrationKey key, ScenePhaseTarget target)
    {
        if (_metadataRegistry.Resolve(key) is null)
        {
            throw new OrchestrationKeyNotFoundException(key);
        }        _targetRegistry.Register(key, target);
    }

    /// <inheritdoc />
    public void RemovePhaseTarget(OrchestrationKey key, ScenePhaseTarget target)
    {
        if (_metadataRegistry.Resolve(key) is null)
        {
            throw new OrchestrationKeyNotFoundException(key);
        }        _targetRegistry.Unregister(key, target);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScenePhase> GetPhases(string sceneId)
    {
        var scene = Get(sceneId);
        if (scene is null)
            throw new KeyNotFoundException($"Scene with ID '{sceneId}' was not found.");
        
        return scene.Phases; 
    }

    /// <inheritdoc />
    public IEnumerable<ScenePhaseTarget> GetTargets(OrchestrationKey key)
    {
        if (_metadataRegistry.Resolve(key) is null)
        {
            throw new OrchestrationKeyNotFoundException(key);
        }        return _targetRegistry.Resolve(key).AsEnumerable();
    }

    /// <inheritdoc />
    public bool IsTargetRegistered(OrchestrationKey key)
    {
        var target = _targetRegistry.ResolveFirstOrDefault(key);
        return Get(key) is not null && target is not null;
    }

    /// <inheritdoc />
    public void RegisterComponent(object component)
    {
        // Targets are bound by method-level [SceneMethod] attributes (the reconciled model: the class's
        // [ScenePhase] attributes define the phases — see DefaultMetadataFactory.CreateFrom — and each
        // [SceneMethod] binds the annotated method to one of those phases).
        var type = component.GetType();
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var attr in method.GetCustomAttributes<SceneMethodAttribute>())
            {
                var key = new OrchestrationKey(attr.SceneId, attr.PhaseId);
                var target = new ScenePhaseTarget(component, method.Name);
                AddPhaseTarget(key, target);
            }
        }
    }

    /// <inheritdoc />
    public bool? UnregisterComponent(object component)
    {
        return _targetRegistry.Unregister(component);
    }

    public void Register<T>() where T : notnull
    {
        var type = typeof(T);
        Register(type);
    }

    public void Register(Type type)
    {
        var sceneMetadata = _metadataFactory.CreateFrom(type);
        _metadataRegistry.Register(sceneMetadata);
    }

   
}
