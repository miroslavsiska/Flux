using Flux.Core.Models;

namespace Flux.Core.Abstractions;

/// <summary>
/// Executes <see cref="IWorkflow{TContext}"/> instances and returns a
/// <see cref="WorkflowResult"/> describing the outcome.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Runs every step in <paramref name="workflow"/> in sequence using the
    /// supplied <paramref name="context"/> and returns a consolidated result.
    /// </summary>
    Task<WorkflowResult> ExecuteAsync<TContext>(
        IWorkflow<TContext> workflow,
        TContext context,
        CancellationToken cancellationToken = default);
}
