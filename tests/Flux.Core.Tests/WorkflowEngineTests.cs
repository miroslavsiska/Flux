using Flux.Core.Builder;
using Flux.Core.Builder.Internal;
using Flux.Core.Engine;
using Flux.Core.Models;

namespace Flux.Core.Tests;

public sealed class WorkflowEngineTests
{
    private readonly WorkflowEngine _engine = new();

    // -------------------------------------------------------------------------
    // Null argument guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NullWorkflow_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _engine.ExecuteAsync<MyContext>(null!, new MyContext()));
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_ThrowsArgumentNullException()
    {
        var wf = new WorkflowBuilder<MyContext>("wf")
            .AddStep("s", _ => StepResult.Success())
            .Build();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _engine.ExecuteAsync(wf, null!));
    }

    // -------------------------------------------------------------------------
    // Sequential happy-path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_ReturnsSuccess()
    {
        var order = new List<string>();
        var wf = new WorkflowBuilder<MyContext>("sequential-wf")
            .AddStep("step-1", _ => { order.Add("step-1"); return StepResult.Success(); })
            .AddStep("step-2", _ => { order.Add("step-2"); return StepResult.Success(); })
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext());

        Assert.True(result.IsSuccess);
        Assert.Equal("sequential-wf", result.WorkflowName);
        Assert.Equal(2, result.StepResults.Count);
        Assert.Equal(["step-1", "step-2"], order);
    }

    // -------------------------------------------------------------------------
    // Failure short-circuit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StepFails_StopsAndReturnsFailed()
    {
        var executed = new List<string>();
        var wf = new WorkflowBuilder<MyContext>("failing-wf")
            .AddStep("ok", _ => { executed.Add("ok"); return StepResult.Success(); })
            .AddStep("fail", _ => { executed.Add("fail"); return StepResult.Failure("boom"); })
            .AddStep("never", _ => { executed.Add("never"); return StepResult.Success(); })
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext());

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.StepResults.Count);
        Assert.DoesNotContain("never", executed);
    }

    // -------------------------------------------------------------------------
    // Unhandled exception in step
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StepThrows_CapturesExceptionAndReturnsFailed()
    {
        var wf = new WorkflowBuilder<MyContext>("throw-wf")
            .AddStep("bad", (_, _) => throw new InvalidOperationException("oops"))
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.StepResults["bad"].Exception);
        Assert.IsType<InvalidOperationException>(result.StepResults["bad"].Exception);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ReturnsCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var wf = new WorkflowBuilder<MyContext>("cancel-wf")
            .AddStep("s", _ => StepResult.Success())
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext(), cts.Token);

        Assert.False(result.IsSuccess);
        Assert.IsType<OperationCanceledException>(result.Exception);
    }

    // -------------------------------------------------------------------------
    // Parallel step
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ParallelStepsAllSucceed_ReturnsSuccess()
    {
        var ctx = new MyContext();
        var wf = new WorkflowBuilder<MyContext>("parallel-wf")
            .AddParallelSteps("group",
                new ActionStep<MyContext>("p1", (_, _) => Task.FromResult(StepResult.Success())),
                new ActionStep<MyContext>("p2", (_, _) => Task.FromResult(StepResult.Success())))
            .Build();

        var result = await _engine.ExecuteAsync(wf, ctx);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_ParallelStepsFail_ReturnsFailure()
    {
        var wf = new WorkflowBuilder<MyContext>("parallel-fail-wf")
            .AddParallelSteps("group",
                new ActionStep<MyContext>("p1", (_, _) => Task.FromResult(StepResult.Success())),
                new ActionStep<MyContext>("p2", (_, _) => Task.FromResult(StepResult.Failure("p2 failed"))))
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext());

        Assert.False(result.IsSuccess);
        Assert.Contains("p2 failed", result.StepResults["group"].ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // Conditional step
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ConditionTrue_RunsThenStep()
    {
        var ctx = new MyContext { Value = 10 };
        bool thenExecuted = false;
        bool elseExecuted = false;

        var thenStep = new ActionStep<MyContext>("then", (_, _) =>
        {
            thenExecuted = true;
            return Task.FromResult(StepResult.Success());
        });
        var elseStep = new ActionStep<MyContext>("else", (_, _) =>
        {
            elseExecuted = true;
            return Task.FromResult(StepResult.Success());
        });

        var wf = new WorkflowBuilder<MyContext>("cond-wf")
            .AddConditionalStep("check", c => c.Value > 5, thenStep, elseStep)
            .Build();

        var result = await _engine.ExecuteAsync(wf, ctx);

        Assert.True(result.IsSuccess);
        Assert.True(thenExecuted);
        Assert.False(elseExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionFalse_RunsElseStep()
    {
        var ctx = new MyContext { Value = 1 };
        bool thenExecuted = false;
        bool elseExecuted = false;

        var thenStep = new ActionStep<MyContext>("then", (_, _) =>
        {
            thenExecuted = true;
            return Task.FromResult(StepResult.Success());
        });
        var elseStep = new ActionStep<MyContext>("else", (_, _) =>
        {
            elseExecuted = true;
            return Task.FromResult(StepResult.Success());
        });

        var wf = new WorkflowBuilder<MyContext>("cond-else-wf")
            .AddConditionalStep("check", c => c.Value > 5, thenStep, elseStep)
            .Build();

        var result = await _engine.ExecuteAsync(wf, ctx);

        Assert.True(result.IsSuccess);
        Assert.False(thenExecuted);
        Assert.True(elseExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionFalse_NoElseStep_Succeeds()
    {
        var ctx = new MyContext { Value = 1 };

        var wf = new WorkflowBuilder<MyContext>("cond-no-else-wf")
            .AddConditionalStep("check", c => c.Value > 5, new ActionStep<MyContext>("then",
                (_, _) => Task.FromResult(StepResult.Success())))
            .Build();

        var result = await _engine.ExecuteAsync(wf, ctx);
        Assert.True(result.IsSuccess);
    }

    // -------------------------------------------------------------------------
    // Retry step
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RetryStep_EventuallySucceeds()
    {
        int attempts = 0;
        var inner = new ActionStep<MyContext>("flaky", (_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts < 3
                ? StepResult.Failure("not yet")
                : StepResult.Success());
        });

        var wf = new WorkflowBuilder<MyContext>("retry-wf")
            .AddRetryStep(inner, maxAttempts: 3, initialDelay: TimeSpan.Zero)
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_RetryStep_ExceedsMaxAttempts_ReturnsFailed()
    {
        int attempts = 0;
        var inner = new ActionStep<MyContext>("always-fails", (_, _) =>
        {
            attempts++;
            return Task.FromResult(StepResult.Failure("always bad"));
        });

        var wf = new WorkflowBuilder<MyContext>("retry-fail-wf")
            .AddRetryStep(inner, maxAttempts: 3, initialDelay: TimeSpan.Zero)
            .Build();

        var result = await _engine.ExecuteAsync(wf, new MyContext());

        Assert.False(result.IsSuccess);
        Assert.Equal(3, attempts);
        Assert.Contains("3 attempt(s)", result.StepResults["always-fails"].ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // Context mutation across steps
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StepsMutateContext_ChangesVisible()
    {
        var ctx = new MyContext { Value = 0 };

        var wf = new WorkflowBuilder<MyContext>("ctx-wf")
            .AddStep("inc-1", c => { c.Value += 1; return StepResult.Success(); })
            .AddStep("inc-2", c => { c.Value += 1; return StepResult.Success(); })
            .AddStep("verify", c => c.Value == 2 ? StepResult.Success() : StepResult.Failure("unexpected value"))
            .Build();

        var result = await _engine.ExecuteAsync(wf, ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, ctx.Value);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed class MyContext { public int Value { get; set; } }
}
