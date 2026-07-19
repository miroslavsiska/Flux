using Flux.Core.Abstractions;
using Flux.Core.Models;

namespace Flux.Core.Builder.Internal;

/// <summary>
/// Executes a collection of steps concurrently and returns success only when all succeed.
/// </summary>
internal sealed class ParallelStep<TContext>(
    string name,
    IReadOnlyList<IStep<TContext>> steps) : IStep<TContext>
{
    public string Name { get; } = name;

    public async Task<StepResult> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
    {
        var tasks = steps.Select(s => s.ExecuteAsync(context, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var failures = results.Where(r => !r.IsSuccess).ToList();
        if (failures.Count == 0)
            return StepResult.Success();

        var message = string.Join("; ", failures.Select(f => f.ErrorMessage));
        return StepResult.Failure($"One or more parallel steps failed: {message}");
    }
}
