namespace Flux.Core.Models;

/// <summary>
/// Describes the outcome of a single workflow step.
/// </summary>
/// <param name="IsSuccess">Whether the step completed without error.</param>
/// <param name="ErrorMessage">Human-readable description of the failure, if any.</param>
/// <param name="Exception">The underlying exception, if one was thrown.</param>
public sealed record StepResult(bool IsSuccess, string? ErrorMessage = null, Exception? Exception = null)
{
    /// <summary>Creates a successful <see cref="StepResult"/>.</summary>
    public static StepResult Success() => new(true);

    /// <summary>Creates a failed <see cref="StepResult"/> from a message.</summary>
    public static StepResult Failure(string errorMessage) =>
        new(false, errorMessage);

    /// <summary>Creates a failed <see cref="StepResult"/> from an exception.</summary>
    public static StepResult Failure(Exception exception) =>
        new(false, exception.Message, exception);

    /// <summary>Creates a failed <see cref="StepResult"/> from a message and exception.</summary>
    public static StepResult Failure(string errorMessage, Exception exception) =>
        new(false, errorMessage, exception);
}
