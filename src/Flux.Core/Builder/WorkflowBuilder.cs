using Flux.Core.Abstractions;
using Flux.Core.Builder.Internal;
using Flux.Core.Models;

namespace Flux.Core.Builder;

/// <summary>
/// Fluent builder for constructing declarative, type-safe <see cref="IWorkflow{TContext}"/>
/// instances without touching the execution engine directly.
/// </summary>
/// <typeparam name="TContext">The shared mutable context passed through every step.</typeparam>
public sealed class WorkflowBuilder<TContext>
{
    private readonly string _name;
    private readonly List<IStep<TContext>> _steps = [];

    /// <summary>Initialises a new builder with the given workflow <paramref name="name"/>.</summary>
    public WorkflowBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    // -------------------------------------------------------------------------
    // Step registration
    // -------------------------------------------------------------------------

    /// <summary>Appends a pre-built <see cref="IStep{TContext}"/> instance.</summary>
    public WorkflowBuilder<TContext> AddStep(IStep<TContext> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        return this;
    }

    /// <summary>
    /// Appends a named step backed by a synchronous delegate.
    /// The delegate receives the context and returns a <see cref="StepResult"/>.
    /// </summary>
    public WorkflowBuilder<TContext> AddStep(string name, Func<TContext, StepResult> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        _steps.Add(new ActionStep<TContext>(name, (ctx, _) => Task.FromResult(action(ctx))));
        return this;
    }

    /// <summary>
    /// Appends a named step backed by an asynchronous delegate.
    /// </summary>
    public WorkflowBuilder<TContext> AddStep(
        string name,
        Func<TContext, CancellationToken, Task<StepResult>> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        _steps.Add(new ActionStep<TContext>(name, action));
        return this;
    }

    /// <summary>
    /// Appends a step that executes <paramref name="steps"/> concurrently and
    /// succeeds only when every inner step succeeds.
    /// </summary>
    /// <param name="groupName">Display name for the parallel group.</param>
    /// <param name="steps">The steps to run in parallel.</param>
    public WorkflowBuilder<TContext> AddParallelSteps(string groupName, params IStep<TContext>[] steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Length == 0) throw new ArgumentException("At least one step is required.", nameof(steps));

        _steps.Add(new ParallelStep<TContext>(groupName, steps));
        return this;
    }

    /// <summary>
    /// Appends a conditional step: executes <paramref name="thenStep"/> when
    /// <paramref name="condition"/> is <see langword="true"/>; executes
    /// <paramref name="elseStep"/> (if provided) otherwise.
    /// </summary>
    public WorkflowBuilder<TContext> AddConditionalStep(
        string name,
        Func<TContext, bool> condition,
        IStep<TContext> thenStep,
        IStep<TContext>? elseStep = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(thenStep);
        _steps.Add(new ConditionalStep<TContext>(name, condition, thenStep, elseStep));
        return this;
    }

    /// <summary>
    /// Wraps <paramref name="step"/> with automatic retry semantics using
    /// exponential back-off, then appends the resulting step.
    /// </summary>
    /// <param name="step">The step to retry.</param>
    /// <param name="maxAttempts">Total number of attempts (including the first). Must be ≥ 1.</param>
    /// <param name="initialDelay">
    /// Delay before the second attempt. Doubles on every subsequent retry.
    /// Pass <see cref="TimeSpan.Zero"/> for no delay.
    /// </param>
    public WorkflowBuilder<TContext> AddRetryStep(
        IStep<TContext> step,
        int maxAttempts = 3,
        TimeSpan initialDelay = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be at least 1.");

        _steps.Add(new RetryStep<TContext>(step.Name, step, maxAttempts, initialDelay));
        return this;
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Constructs and returns the immutable <see cref="IWorkflow{TContext}"/>.
    /// </summary>
    public IWorkflow<TContext> Build()
    {
        if (_steps.Count == 0)
            throw new InvalidOperationException("A workflow must contain at least one step.");

        return new Workflow<TContext>(_name, _steps.AsReadOnly());
    }
}
