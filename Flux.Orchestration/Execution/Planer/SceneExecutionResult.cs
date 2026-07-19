namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// The outcome of a direct (synchronous) scene execution via <see cref="IPlanner.ExecuteSceneAsync(string, Flux.Orchestration.Model.SceneContext, bool, System.Threading.CancellationToken)"/>.
/// Carries what was (or, for a dry-run, would have been) run, so a caller can preview cost/shape.
/// </summary>
/// <param name="Levels">Number of compiled DAG levels walked.</param>
/// <param name="Phases">Number of phases that had at least one registered target.</param>
/// <param name="Targets">Total phase-targets executed (or, on a dry-run, that WOULD have executed).</param>
/// <param name="DryRun">True when no target was actually invoked — the levels were walked for foresight only.</param>
public sealed record SceneExecutionResult(int Levels, int Phases, int Targets, bool DryRun);
