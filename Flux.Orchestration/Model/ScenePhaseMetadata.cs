using Flux.Orchestration.Model.Base;

namespace Flux.Orchestration.Model;

public sealed class ScenePhaseMetadata : ScenePhaseBase
{
    internal ScenePhase ToScenePhase(IReadOnlyList<ScenePhaseTarget> targets)
    {
        return new ScenePhase(
                PhaseId,
                targets,
                Description,
                Category,
                Tags,
                Priority,
                Parallel,
                Timeout,
                MaxRetries,
                Logging,
                Parameters)
        {
            DependsOn = DependsOn,
            Reads = Reads,
            Writes = Writes,
            SequentialTargets = SequentialTargets,
        };
    }


    /// <summary>
    /// Creates phase metadata from its id and optional configuration.
    /// </summary>
    /// <param name="phaseId">Unique phase identifier; not null or empty.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="category">Optional grouping category.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="priority">Priority level.</param>
    /// <param name="parallel">Whether the phase may run in parallel.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="maxRetries">Max retry attempts on failure.</param>
    /// <param name="logging">Whether logging is enabled.</param>
    /// <param name="parameters">Optional parameters; defaults to empty.</param>
    public ScenePhaseMetadata(
       string phaseId,
       string? description = null,
       string? category = null,
       IReadOnlyList<string>? tags = null,
       int priority = 0,
       bool parallel = false,
       TimeSpan? timeout = null,
       int maxRetries = 0,
       bool logging = true,
       Dictionary<string, object>? parameters = null)
        : base(
              phaseId,
              description,
              category,
              tags,
              priority,
              parallel,
              timeout,
              maxRetries,
              logging,
              parameters)
    {
        //MethodName = methodName;
        //Method = method;
    }
}
