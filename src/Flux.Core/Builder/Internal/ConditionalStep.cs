using Flux.Core.Abstractions;
using Flux.Core.Models;

namespace Flux.Core.Builder.Internal;

/// <summary>
/// Executes <see cref="ThenStep"/> when <see cref="_condition"/> evaluates to <see langword="true"/>;
/// optionally executes <see cref="ElseStep"/> otherwise.
/// </summary>
internal sealed class ConditionalStep<TContext>(
    string name,
    Func<TContext, bool> condition,
    IStep<TContext> thenStep,
    IStep<TContext>? elseStep = null) : IStep<TContext>
{
    internal IStep<TContext> ThenStep { get; } = thenStep;
    internal IStep<TContext>? ElseStep { get; } = elseStep;

    public string Name { get; } = name;

    public Task<StepResult> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
        => condition(context)
            ? ThenStep.ExecuteAsync(context, cancellationToken)
            : ElseStep is not null
                ? ElseStep.ExecuteAsync(context, cancellationToken)
                : Task.FromResult(StepResult.Success());
}
