using Flux.Core.Abstractions;
using Flux.Core.Models;

namespace Flux.Core.Builder.Internal;

/// <summary>
/// Wraps a step with automatic retry semantics using exponential back-off.
/// </summary>
internal sealed class RetryStep<TContext>(
    string name,
    IStep<TContext> inner,
    int maxAttempts,
    TimeSpan initialDelay) : IStep<TContext>
{
    internal IStep<TContext> InnerStep { get; } = inner;
    internal int MaxAttempts { get; } = maxAttempts;
    internal TimeSpan InitialDelay { get; } = initialDelay;

    public string Name { get; } = name;

    public async Task<StepResult> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
    {
        StepResult result = StepResult.Failure("No attempts made.");
        TimeSpan delay = InitialDelay;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await InnerStep.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
                return result;

            if (attempt < MaxAttempts && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * 2);
            }
        }

        return StepResult.Failure(
            $"Step '{InnerStep.Name}' failed after {MaxAttempts} attempt(s). Last error: {result.ErrorMessage}",
            result.Exception!);
    }
}
