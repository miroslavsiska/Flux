using System.Diagnostics;
using Flux.Core.Abstractions;
using Flux.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Core.Engine;

/// <summary>
/// Default implementation of <see cref="IWorkflowEngine"/>.
/// Executes the steps of a workflow sequentially, stopping on the first failure.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly ILogger<WorkflowEngine> _logger;

    /// <summary>
    /// Initialises the engine with an optional <paramref name="logger"/>.
    /// If no logger is supplied, a no-op logger is used.
    /// </summary>
    public WorkflowEngine(ILogger<WorkflowEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<WorkflowEngine>.Instance;
    }

    /// <inheritdoc/>
    public async Task<WorkflowResult> ExecuteAsync<TContext>(
        IWorkflow<TContext> workflow,
        TContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation("Starting workflow '{WorkflowName}' with {StepCount} step(s).",
            workflow.Name, workflow.Steps.Count);

        var stepResults = new Dictionary<string, StepResult>(workflow.Steps.Count);
        var sw = Stopwatch.StartNew();

        try
        {
            foreach (var step in workflow.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Executing step '{StepName}'.", step.Name);

                StepResult stepResult;
                try
                {
                    stepResult = await step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Step '{StepName}' threw an unhandled exception.", step.Name);
                    stepResult = StepResult.Failure($"Unhandled exception in step '{step.Name}'.", ex);
                }

                stepResults[step.Name] = stepResult;

                if (!stepResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Step '{StepName}' failed: {ErrorMessage}. Aborting workflow.",
                        step.Name, stepResult.ErrorMessage);

                    sw.Stop();
                    return WorkflowResult.Failure(workflow.Name, stepResults, sw.Elapsed, stepResult.Exception);
                }

                _logger.LogDebug("Step '{StepName}' succeeded.", step.Name);
            }

            sw.Stop();
            _logger.LogInformation(
                "Workflow '{WorkflowName}' completed successfully in {Elapsed}.",
                workflow.Name, sw.Elapsed);

            return WorkflowResult.Success(workflow.Name, stepResults, sw.Elapsed);
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();
            _logger.LogWarning("Workflow '{WorkflowName}' was cancelled.", workflow.Name);
            return WorkflowResult.Failure(workflow.Name, stepResults, sw.Elapsed, ex);
        }
    }
}
