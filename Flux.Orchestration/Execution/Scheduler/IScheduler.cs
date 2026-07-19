using Flux.Orchestration.Model;

namespace Flux.Orchestration.Execution.Scheduler;

/// <summary>
/// Orchestrates when and how phases run (parallel vs sequential, retries, timeouts).
/// </summary>
public interface IScheduler
{
    /// <summary>Schedules and executes the phase manifests.</summary>
    /// <remarks>Manifests are processed in the order provided; cancellation terminates early.</remarks>
    /// <param name="phaseManifests">The phases to execute.</param>
    /// <param name="cancellationToken">Token observed during execution.</param>
    /// <returns>A task that completes when scheduling finishes.</returns>
    Task ScheduleAsync(IEnumerable<ScenePhaseManifest> phaseManifests, CancellationToken cancellationToken = default);
}
