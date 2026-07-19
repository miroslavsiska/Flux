using Flux.Orchestration.Model;

namespace Flux.Orchestration.Builders;

/// <summary>
/// Fluent builder for creating and configuring a <see cref="ScenePhase"/>.
/// </summary>
public sealed class ScenePhaseBuilder
{
    private string _phaseId = default!;
    private List<ScenePhaseTarget>? _targets;
    private string? _description;
    private readonly Dictionary<string, object> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private int _priority = default;
    private bool _parallel = false;
    private TimeSpan _timeout = default;
    private int _maxRetries = 0;
    private bool _logging;

    /// <summary>
    /// Sets the phase identifier for the scene phase and returns the updated builder instance.
    /// </summary>
    /// <param name="phaseId">The unique identifier for the phase. Cannot be <see langword="null"/> or empty.</param>
    /// <returns>The current <see cref="ScenePhaseBuilder"/> instance with the updated phase identifier.</returns>
    public ScenePhaseBuilder WithPhaseId(string phaseId)
    {
        _phaseId = phaseId;
        return this;
    }

    /// <summary>
    /// Sets the priority for the scene phase and returns the updated builder.
    /// </summary>
    /// <param name="priority">The priority value to assign. Defaults to <see langword="default"/> if not specified.</param>
    /// <returns>The updated <see cref="ScenePhaseBuilder"/> instance with the specified priority.</returns>
    public ScenePhaseBuilder WithPriority(int priority = default)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Marks the phase to run its targets in parallel.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    public ScenePhaseBuilder AsParallel()
    {
        _parallel = true;
        return this;
    }
 
    /// <summary>
    /// Sets the timeout duration for the scene phase.
    /// </summary>
    /// <param name="timeout">The maximum duration to wait before the scene phase times out.</param>
    /// <returns>The current <see cref="ScenePhaseBuilder"/> instance, allowing for method chaining.</returns>
    public ScenePhaseBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the max retry attempts for the phase; 0 disables retries.
    /// </summary>
    /// <param name="maxRetries">Non-negative retry count; default 3.</param>
    /// <returns>This builder, for chaining.</returns>
    public ScenePhaseBuilder WithRetries(int maxRetries = 3)
    {
        _maxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// Enables logging for the phase.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    public ScenePhaseBuilder WithLogging()
    {
        _logging = true;
        return this;
    }

    /// <summary>
    /// Adds or updates a parameter for the scene phase.
    /// </summary>
    /// <param name="key">The key that identifies the parameter. Cannot be <see langword="null"/> or empty.</param>
    /// <param name="value">The value to associate with the specified key. Can be <see langword="null"/>.</param>
    /// <returns>The current <see cref="ScenePhaseBuilder"/> instance, allowing for method chaining.</returns>
    public ScenePhaseBuilder WithParameter(string key, object value)
    {
        _parameters[key] = value;
        return this;
    }

    /// <summary>
    /// Adds or updates the parameters for the scene phase.
    /// </summary>
    /// <param name="parameters">A dictionary containing the parameters to add or update. The keys represent parameter names, and the values
    /// represent their corresponding values. Existing parameters with matching keys will be overwritten.</param>
    /// <returns>The current <see cref="ScenePhaseBuilder"/> instance, allowing for method chaining.</returns>
    public ScenePhaseBuilder WithParameters(IDictionary<string, object> parameters)
    {
        foreach (var item in parameters)
        {
            _parameters[item.Key] = item.Value; // overwrite existing
        }
        return this;
    }

    /// <summary>
    /// Sets the description for the scene phase.
    /// </summary>
    /// <param name="description">The description to associate with the scene phase. Cannot be null or empty.</param>
    /// <returns>The current instance of <see cref="ScenePhaseBuilder"/> to allow method chaining.</returns>
    public ScenePhaseBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>
    /// Adds a target instance to the scene phase.
    /// </summary>
    /// <param name="target">The target object to invoke.</param>
    /// <param name="methodName">Optional method name to invoke on the target.</param>
    /// <returns>This builder, for chaining.</returns>
    public ScenePhaseBuilder AddTarget(object target, string? methodName = null)
    {
        _targets ??= [];
        _targets.Add(new ScenePhaseTarget(target, methodName));
        return this;
    }

    /// <summary>
    /// Adds a target to the scene phase.
    /// </summary>
    /// <param name="target">The target to add.</param>
    /// <returns>This builder, for chaining.</returns>
    public ScenePhaseBuilder AddTarget(ScenePhaseTarget target)
    {
        _targets ??= [];
        _targets.Add(target);
        return this;
    }

    /// <summary>
    /// Builds a <see cref="ScenePhase"/> from the configured values.
    /// </summary>
    /// <returns>The configured <see cref="ScenePhase"/>.</returns>
    public ScenePhase Build()
        => new ScenePhase(
            phaseId: _phaseId,
            targets: _targets,
            description: _description,
            priority: _priority,
            parallel: _parallel,
            timeout: _timeout == default ? null : _timeout,
            maxRetries: _maxRetries,
            logging: _logging,
            parameters: _parameters
        );
}
