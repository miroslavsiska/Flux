using Flux.Core.Abstractions;
using Flux.Core.Models;

namespace Flux.Core.Builder.Internal;

/// <summary>
/// A step backed by a user-supplied delegate.
/// </summary>
internal sealed class ActionStep<TContext>(
    string name,
    Func<TContext, CancellationToken, Task<StepResult>> action) : IStep<TContext>
{
    public string Name { get; } = name;

    public Task<StepResult> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        => action(context, cancellationToken);
}
