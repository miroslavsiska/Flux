namespace Flux.Core.Models;

/// <summary>
/// Describes the consolidated outcome of an entire workflow execution.
/// </summary>
public sealed class WorkflowResult
{
    private WorkflowResult(
        bool isSuccess,
        string workflowName,
        IReadOnlyDictionary<string, StepResult> stepResults,
        Exception? exception,
        TimeSpan elapsed)
    {
        IsSuccess = isSuccess;
        WorkflowName = workflowName;
        StepResults = stepResults;
        Exception = exception;
        Elapsed = elapsed;
    }

    /// <summary>Gets whether every executed step succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the name of the workflow that produced this result.</summary>
    public string WorkflowName { get; }

    /// <summary>
    /// Gets a dictionary mapping each step's name to its individual result.
    /// Steps that were skipped (e.g. due to a prior failure) are not included.
    /// </summary>
    public IReadOnlyDictionary<string, StepResult> StepResults { get; }

    /// <summary>
    /// Gets the unhandled exception that aborted the workflow, if any.
    /// Only set when a step threw unexpectedly outside its own error handling.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>Gets the total wall-clock time taken by the workflow execution.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Creates a successful <see cref="WorkflowResult"/>.</summary>
    public static WorkflowResult Success(
        string workflowName,
        IReadOnlyDictionary<string, StepResult> stepResults,
        TimeSpan elapsed) =>
        new(true, workflowName, stepResults, null, elapsed);

    /// <summary>Creates a failed <see cref="WorkflowResult"/>.</summary>
    public static WorkflowResult Failure(
        string workflowName,
        IReadOnlyDictionary<string, StepResult> stepResults,
        TimeSpan elapsed,
        Exception? exception = null) =>
        new(false, workflowName, stepResults, exception, elapsed);
}
