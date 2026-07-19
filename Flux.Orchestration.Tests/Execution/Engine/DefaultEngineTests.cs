using Flux.Orchestration.Execution.Engine;

namespace Flux.Orchestration.Tests.Execution.Engine;

/// <summary>
/// Unit tests for <see cref="DefaultEngine"/> — the thin, allocation-free invocation layer.
/// All three dispatch paths (Sync / ValueTask / Task) are covered, including null-delegate and
/// cancellation-token propagation scenarios.
/// </summary>
public class DefaultEngineTests
{
    private readonly DefaultEngine _sut = new();
    private readonly SceneContext _ctx = new();

    // ────────────────────────────────────────────────────────────────
    // InvokeSync
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void InvokeSync_WhenDelegateIsProvided_InvokesDelegate()
    {
        // Arrange
        bool invoked = false;
        var target = new object();
        Action<object, SceneContext, CancellationToken> del = (t, c, ct) => invoked = true;

        // Act
        _sut.InvokeSync(target, del, _ctx, CancellationToken.None);

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public void InvokeSync_PassesCorrectTargetAndContext()
    {
        // Arrange
        object? capturedTarget = null;
        SceneContext? capturedCtx = null;
        var expectedTarget = new object();
        var expectedCtx = new SceneContext();

        Action<object, SceneContext, CancellationToken> del = (t, c, ct) =>
        {
            capturedTarget = t;
            capturedCtx = c;
        };

        // Act
        _sut.InvokeSync(expectedTarget, del, expectedCtx, CancellationToken.None);

        // Assert
        Assert.Same(expectedTarget, capturedTarget);
        Assert.Same(expectedCtx, capturedCtx);
    }

    [Fact]
    public void InvokeSync_PropagatesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        CancellationToken captured = default;

        Action<object, SceneContext, CancellationToken> del = (_, _, ct) => captured = ct;

        // Act
        _sut.InvokeSync(new object(), del, _ctx, cts.Token);

        // Assert
        Assert.True(captured.IsCancellationRequested);
    }

    [Fact]
    public void InvokeSync_WhenDelegateThrows_ExceptionPropagates()
    {
        // Arrange
        Action<object, SceneContext, CancellationToken> del = (_, _, _) =>
            throw new InvalidOperationException("sync-boom");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _sut.InvokeSync(new object(), del, _ctx, CancellationToken.None));
    }

    // ────────────────────────────────────────────────────────────────
    // InvokeValueTaskAsync
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeValueTaskAsync_WhenDelegateIsProvided_InvokesDelegate()
    {
        // Arrange
        bool invoked = false;
        Func<object, SceneContext, CancellationToken, ValueTask> del =
            (_, _, _) => { invoked = true; return ValueTask.CompletedTask; };

        // Act
        await _sut.InvokeValueTaskAsync(new object(), del, _ctx, CancellationToken.None);

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task InvokeValueTaskAsync_PassesCorrectTargetAndContext()
    {
        // Arrange
        object? capturedTarget = null;
        SceneContext? capturedCtx = null;
        var expectedTarget = new object();
        var expectedCtx = new SceneContext();

        Func<object, SceneContext, CancellationToken, ValueTask> del = (t, c, _) =>
        {
            capturedTarget = t;
            capturedCtx = c;
            return ValueTask.CompletedTask;
        };

        // Act
        await _sut.InvokeValueTaskAsync(expectedTarget, del, expectedCtx, CancellationToken.None);

        // Assert
        Assert.Same(expectedTarget, capturedTarget);
        Assert.Same(expectedCtx, capturedCtx);
    }

    [Fact]
    public async Task InvokeValueTaskAsync_WhenDelegateThrows_ExceptionPropagates()
    {
        // Arrange
        Func<object, SceneContext, CancellationToken, ValueTask> del =
            (_, _, _) => ValueTask.FromException(new InvalidOperationException("vt-boom"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.InvokeValueTaskAsync(new object(), del, _ctx, CancellationToken.None).AsTask());
    }

    // ────────────────────────────────────────────────────────────────
    // InvokeTaskAsync
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeTaskAsync_WhenDelegateIsProvided_InvokesDelegate()
    {
        // Arrange
        bool invoked = false;
        Func<object, SceneContext, CancellationToken, Task> del =
            (_, _, _) => { invoked = true; return Task.CompletedTask; };

        // Act
        await _sut.InvokeTaskAsync(new object(), del, _ctx, CancellationToken.None);

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task InvokeTaskAsync_PassesCorrectTargetAndContext()
    {
        // Arrange
        object? capturedTarget = null;
        SceneContext? capturedCtx = null;
        var expectedTarget = new object();
        var expectedCtx = new SceneContext();

        Func<object, SceneContext, CancellationToken, Task> del = (t, c, _) =>
        {
            capturedTarget = t;
            capturedCtx = c;
            return Task.CompletedTask;
        };

        // Act
        await _sut.InvokeTaskAsync(expectedTarget, del, expectedCtx, CancellationToken.None);

        // Assert
        Assert.Same(expectedTarget, capturedTarget);
        Assert.Same(expectedCtx, capturedCtx);
    }

    [Fact]
    public async Task InvokeTaskAsync_WhenDelegateThrows_ExceptionPropagates()
    {
        // Arrange
        Func<object, SceneContext, CancellationToken, Task> del =
            (_, _, _) => Task.FromException(new InvalidOperationException("task-boom"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.InvokeTaskAsync(new object(), del, _ctx, CancellationToken.None));
    }
}
