using Flux.Core.Abstractions;

namespace Flux.Core.Builder.Internal;

/// <summary>Internal concrete implementation of <see cref="IWorkflow{TContext}"/>.</summary>
internal sealed class Workflow<TContext>(string name, IReadOnlyList<IStep<TContext>> steps) : IWorkflow<TContext>
{
    public string Name { get; } = name;
    public IReadOnlyList<IStep<TContext>> Steps { get; } = steps;
}
