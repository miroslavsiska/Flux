using Flux.Core.Models;

namespace Flux.Core.Abstractions;

/// <summary>
/// Represents a single, executable unit of work within a workflow.
/// </summary>
/// <typeparam name="TContext">The shared workflow context type.</typeparam>
public interface IStep<TContext>
{
    /// <summary>Gets the display name of this step.</summary>
    string Name { get; }

    /// <summary>
    /// Executes the step against the provided <paramref name="context"/>.
    /// </summary>
    Task<StepResult> ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}
