using Flux.Core.Abstractions;
using Flux.Core.Builder;
using Flux.Core.Builder.Internal;
using Flux.Core.Models;

namespace Flux.Core.Tests;

public sealed class WorkflowBuilderTests
{
    // -------------------------------------------------------------------------
    // Constructor validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullOrWhiteSpaceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowBuilder<object>(""));
        Assert.Throws<ArgumentException>(() => new WorkflowBuilder<object>("   "));
    }

    // -------------------------------------------------------------------------
    // AddStep (delegate overloads)
    // -------------------------------------------------------------------------

    [Fact]
    public void AddStep_SyncDelegate_AddsStepWithCorrectName()
    {
        var workflow = new WorkflowBuilder<MyContext>("wf")
            .AddStep("init", ctx => StepResult.Success())
            .Build();

        Assert.Single(workflow.Steps);
        Assert.Equal("init", workflow.Steps[0].Name);
    }

    [Fact]
    public void AddStep_AsyncDelegate_AddsStepWithCorrectName()
    {
        var workflow = new WorkflowBuilder<MyContext>("wf")
            .AddStep("load", (ctx, ct) => Task.FromResult(StepResult.Success()))
            .Build();

        Assert.Single(workflow.Steps);
        Assert.Equal("load", workflow.Steps[0].Name);
    }

    [Fact]
    public void AddStep_NullStep_ThrowsArgumentNullException()
    {
        var builder = new WorkflowBuilder<MyContext>("wf");
        Assert.Throws<ArgumentNullException>(() => builder.AddStep((IStep<MyContext>)null!));
    }

    // -------------------------------------------------------------------------
    // AddParallelSteps
    // -------------------------------------------------------------------------

    [Fact]
    public void AddParallelSteps_ValidSteps_AddsParallelStep()
    {
        var s1 = MakeStep("a");
        var s2 = MakeStep("b");

        var workflow = new WorkflowBuilder<MyContext>("wf")
            .AddParallelSteps("parallel-group", s1, s2)
            .Build();

        Assert.Single(workflow.Steps);
        Assert.Equal("parallel-group", workflow.Steps[0].Name);
    }

    [Fact]
    public void AddParallelSteps_NoSteps_ThrowsArgumentException()
    {
        var builder = new WorkflowBuilder<MyContext>("wf");
        Assert.Throws<ArgumentException>(() => builder.AddParallelSteps("pg"));
    }

    // -------------------------------------------------------------------------
    // AddConditionalStep
    // -------------------------------------------------------------------------

    [Fact]
    public void AddConditionalStep_AddsConditionalStep()
    {
        var workflow = new WorkflowBuilder<MyContext>("wf")
            .AddConditionalStep("cond", _ => true, MakeStep("then"))
            .Build();

        Assert.Single(workflow.Steps);
        Assert.Equal("cond", workflow.Steps[0].Name);
    }

    [Fact]
    public void AddConditionalStep_NullCondition_ThrowsArgumentNullException()
    {
        var builder = new WorkflowBuilder<MyContext>("wf");
        Assert.Throws<ArgumentNullException>(() =>
            builder.AddConditionalStep("cond", null!, MakeStep("then")));
    }

    // -------------------------------------------------------------------------
    // AddRetryStep
    // -------------------------------------------------------------------------

    [Fact]
    public void AddRetryStep_ValidStep_AddsRetryStep()
    {
        var inner = MakeStep("inner");
        var workflow = new WorkflowBuilder<MyContext>("wf")
            .AddRetryStep(inner, maxAttempts: 3)
            .Build();

        Assert.Single(workflow.Steps);
        Assert.Equal("inner", workflow.Steps[0].Name);
        var retry = Assert.IsType<RetryStep<MyContext>>(workflow.Steps[0]);
        Assert.Equal(3, retry.MaxAttempts);
    }

    [Fact]
    public void AddRetryStep_ZeroMaxAttempts_ThrowsArgumentOutOfRangeException()
    {
        var builder = new WorkflowBuilder<MyContext>("wf");
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddRetryStep(MakeStep("x"), maxAttempts: 0));
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_EmptyStepList_ThrowsInvalidOperationException()
    {
        var builder = new WorkflowBuilder<MyContext>("wf");
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_SetsWorkflowName()
    {
        var workflow = new WorkflowBuilder<MyContext>("my-workflow")
            .AddStep("s1", _ => StepResult.Success())
            .Build();

        Assert.Equal("my-workflow", workflow.Name);
    }

    [Fact]
    public void Build_MultipleSteps_PreservesOrder()
    {
        var workflow = new WorkflowBuilder<MyContext>("wf")
            .AddStep("first", _ => StepResult.Success())
            .AddStep("second", _ => StepResult.Success())
            .AddStep("third", _ => StepResult.Success())
            .Build();

        Assert.Equal(["first", "second", "third"], workflow.Steps.Select(s => s.Name));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IStep<MyContext> MakeStep(string name, bool succeeds = true)
    {
        return new ActionStep<MyContext>(name, (_, _) =>
            Task.FromResult(succeeds ? StepResult.Success() : StepResult.Failure("fail")));
    }

    private sealed class MyContext { public int Value { get; set; } }
}
