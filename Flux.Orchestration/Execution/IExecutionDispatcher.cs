using System.Collections.Concurrent;

namespace Flux.Orchestration.Execution;

/// <summary>
/// Marshals a scene's per-level dispatch onto a particular execution context (thread). Lets a scene be pinned
/// to a single thread — e.g. a render thread that must own all mutations — without the rest of the orchestrator
/// caring where work runs.
/// </summary>
/// <remarks>
/// The default <see cref="InlineDispatcher"/> runs work on the calling thread (no affinity, zero overhead).
/// <see cref="DedicatedThreadDispatcher"/> pins work — including continuations after <c>await</c> — to one
/// dedicated thread, which is what "render-thread-only" semantics require.
/// </remarks>
public interface IExecutionDispatcher
{
    /// <summary>Runs <paramref name="work"/> on this dispatcher's execution context and completes when it does.</summary>
    ValueTask InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken = default);
}

/// <summary>Default dispatcher: runs work inline on the calling thread. No affinity, no allocation.</summary>
public sealed class InlineDispatcher : IExecutionDispatcher
{
    /// <summary>Shared singleton — the dispatcher is stateless.</summary>
    public static readonly InlineDispatcher Instance = new();

    public ValueTask InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken = default)
        => work();
}

/// <summary>
/// Dispatcher that pins all work to a single dedicated background thread. Continuations after <c>await</c>
/// resume on the same thread (via a private <see cref="SynchronizationContext"/>), so a phase that yields still
/// finishes on the affinity thread — the guarantee a render thread needs.
/// </summary>
public sealed class DedicatedThreadDispatcher : IExecutionDispatcher, IDisposable
{
    private readonly SingleThreadSynchronizationContext _context = new();
    private readonly Thread _thread;
    private int _disposed;

    /// <summary>The managed id of the dedicated thread (useful for affinity assertions/diagnostics).</summary>
    public int ManagedThreadId => _thread.ManagedThreadId;

    public DedicatedThreadDispatcher(string name = "flux-affinity")
    {
        _thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(_context);
            _context.RunOnCurrentThread();
        })
        {
            IsBackground = true,
            Name = name,
        };
        _thread.Start();
    }

    public ValueTask InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        // RunContinuationsAsynchronously avoids the awaiter resuming inline on the dispatcher thread.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(async _ =>
        {
            try
            {
                await work().ConfigureAwait(true); // ConfigureAwait(true): stay on the dedicated thread across awaits
                tcs.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return new ValueTask(tcs.Task);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        _context.Complete();
        if (Thread.CurrentThread != _thread)
            _thread.Join();
    }
}

/// <summary>
/// A minimal single-threaded <see cref="SynchronizationContext"/> with a blocking work queue, pumped by one
/// thread. Posted callbacks (including async continuations) all run on that thread. Based on the well-known
/// single-thread message-pump pattern.
/// </summary>
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        // If the queue is already completed (dispatcher disposed), drop the work rather than throw.
        if (!_queue.IsAddingCompleted)
        {
            try { _queue.Add((d, state)); }
            catch (InvalidOperationException) { /* completed concurrently — drop */ }
        }
    }

    /// <summary>Pumps queued callbacks on the current thread until <see cref="Complete"/> is called.</summary>
    public void RunOnCurrentThread()
    {
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }

    /// <summary>Signals the pump to drain remaining work and exit.</summary>
    public void Complete() => _queue.CompleteAdding();
}
