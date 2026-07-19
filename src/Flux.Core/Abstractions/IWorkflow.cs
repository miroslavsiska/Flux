namespace Flux.Core.Abstractions;

/// <summary>
/// Represents an immutable, declarative workflow composed of ordered steps.
/// </summary>
/// <typeparam name="TContext">The shared workflow context type.</typeparam>
public interface IWorkflow<TContext>
{
    /// <summary>Gets the display name of this workflow.</summary>
    string Name { get; }

    /// <summary>Gets the ordered list of steps that make up this workflow.</summary>
    IReadOnlyList<IStep<TContext>> Steps { get; }
}
