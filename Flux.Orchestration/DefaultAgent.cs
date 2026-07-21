using Flux.Orchestration.Exceptions;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Flux.Orchestration.Runtime;
using Microsoft.Extensions.Logging;

namespace Flux.Orchestration;

/// <summary>Default <see cref="IAgent"/>: registers and executes scenes and phases over an <see cref="IRegistry"/> and <see cref="IPlanner"/>.</summary>
public class DefaultAgent : IAgent, IDisposable, IAsyncDisposable
{
    private readonly IRegistry _registry;
    private readonly IPlanner _planer;
    private readonly ILogger<DefaultAgent> _logger;
    private readonly ObjectDisposalTokenSource _disposalTokenSource;
    private readonly ObjectDisposalToken _disposalToken;
    private bool _disposed;

    /// <summary>Creates an agent over the given registry, planner, and logger.</summary>
    /// <param name="registry">The orchestration registry.</param>
    /// <param name="planer">The planner that executes scenes.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAgent(IRegistry registry, IPlanner planer, ILogger<DefaultAgent> logger)
    {
        _registry = registry;
        _planer = planer;
        _logger = logger;
        _disposalTokenSource = new();
        _disposalToken = _disposalTokenSource.Token;
    }

    /// <inheritdoc />
    public void RegisterScene(Scene scene)
    {
        _registry.Add(scene);
    }

    /// <inheritdoc />
    public void RegisterPhaseTarget(string sceneId, string phaseId, object instance, string? phaseOrMethodName)
    {
        var key = new OrchestrationKey(sceneId, phaseId);
        RegisterPhaseTarget(key, instance, phaseOrMethodName);
    }

    public void RegisterPhaseTarget(OrchestrationKey key, object instance, string? phaseOrMethodName)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance), "Instance object cannot be null.");

        if (_registry.Get(key) is null)
        {
            throw new OrchestrationKeyNotFoundException(key);
        }

        _registry.AddPhaseTarget(key, instance, phaseOrMethodName);
        RegisterDisposalTokens(instance);
    }


    /// <inheritdoc />
    public void RegisterComponent(object component)
    {
        _registry.RegisterComponent(component);
        RegisterDisposalTokens(component);
    }

    /// <inheritdoc />
    public bool IsPhaseRegistered(string sceneId, string phaseId)
    {
        var key = new OrchestrationKey(sceneId, phaseId);
        return IsPhaseRegistered(key);
    }

    public bool IsPhaseRegistered(OrchestrationKey key)
       => _registry.IsTargetRegistered(key);

    /// <inheritdoc />
    public bool IsSceneRegistered(string sceneId)
        => _registry.IsRegistered(sceneId);

    /// <inheritdoc />
    public Task ExecuteSceneAsync(string sceneId, SceneContext context, CancellationToken cancellationToken = default)
    {
        // Synchronous execution to completion (direct scheduler-walk, recursion-safe) — see IPlanner.ExecuteSceneAsync.
        // For fire-and-forget tick-driven planning, call IPlanner.PlanSceneAsync instead.
        return _planer.ExecuteSceneAsync(sceneId, context, dryRun: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task ExecuteSceneAsync(Scene scene, SceneContext context, CancellationToken cancellationToken = default)
    {
        if (!_registry.IsRegistered(scene.Id))
        {
            _registry.Add(scene);
        }

        return _planer.ExecuteSceneAsync(scene.Id, context, dryRun: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task PlanSignalAsync(string signal, SceneContext context, CancellationToken cancellationToken = default)
    {
        return _planer.PlanSignalAsync(signal, context, cancellationToken);
    }

    /// <inheritdoc />
    public Task ExecuteSignalAsync(string signal, SceneContext context, CancellationToken cancellationToken = default)
    {
        // Synchronous to completion (highest-priority scene first), via the same direct scheduler-walk as ExecuteScene.
        return _planer.ExecuteSignalAsync(signal, context, dryRun: false, cancellationToken);
    }

    /// <summary>
    /// Links the given instances to the agent's disposal token so they are unregistered when the agent is disposed.
    /// </summary>
    /// <typeparam name="T">The instance type.</typeparam>
    /// <param name="instances">Instances to unregister on agent disposal. Null entries are skipped.</param>
    protected void RegisterDisposalTokens<T>(params T[] instances)
    {
        if (instances == null || instances.Length == 0)
            return;

        foreach (var val in instances)
        {
            if (val is null)
                continue;

            _disposalToken.CreateLinkedDisposalToken(val, token =>
            {
                HandleDisposal(val, token);
            });
        }
    }

    private void HandleDisposal<T>(T val, ObjectDisposalToken token)
    {
        _logger.LogTrace($"[DisposalToken] Starting auto-unregistering the instance '{val!.GetType().Name}' from orchestration registry.");

        if (token.Instance is T typedInstance)
        {
            var result = _registry.UnregisterComponent(val!);
            if (result != true)
            {
                _logger.LogWarning($"[DisposalToken] Instance '{typedInstance.GetType().Name}' can't be unregistered from orchestration registry.");
            }
            _logger.LogTrace($"[DisposalToken] Instance '{typedInstance.GetType().Name}' disposed and unregistered from orchestration registry.");
        }
        else
        {
            throw new InvalidOperationException($"Disposal token instance type mismatch. Expected '{typeof(T).Name}', got '{token.Instance?.GetType().Name}'.");
        }
    }

    #region Disposal Logic

    /// <summary>Synchronous agent disposal.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Asynchronous agent disposal. Disposes all linked components before the agent itself.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _logger.LogTrace("[Agent] Starting asynchronous disposal of the agent and all linked components.");

        // 1. First, run the fixed asynchronous cleanup on the token (parallel WhenAll)
        await _disposalToken.DisposeWithInstancesAsync();

        // 2. Then clean up the source itself
        _disposalTokenSource.Dispose();

        _disposed = true;

        GC.SuppressFinalize(this);
        _logger.LogDebug("[Agent] Agent and all linked components have been asynchronously disposed.");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _logger.LogTrace("[Agent] Starting synchronous disposal of the agent.");

            // This will trigger a chain reaction:
            // Source.Dispose() -> Token.Dispose() -> HandleDisposal (Unregister from Registry)
            _disposalTokenSource.Dispose();
        }

        _disposed = true;
        _logger.LogDebug("[Agent] Agent and all linked components have been synchronously disposed.");
    }

    #endregion

}
