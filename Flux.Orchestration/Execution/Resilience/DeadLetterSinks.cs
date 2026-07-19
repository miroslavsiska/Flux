using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Flux.Orchestration.Execution.Resilience;

/// <summary>Dead-letter sink that logs each failed phase at error level.</summary>
public sealed class LoggingDeadLetterSink : IDeadLetterSink
{
    private readonly ILogger<LoggingDeadLetterSink> _logger;

    public LoggingDeadLetterSink(ILogger<LoggingDeadLetterSink> logger) => _logger = logger;

    public ValueTask HandleAsync(DeadLetterContext context, CancellationToken cancellationToken)
    {
        _logger.LogError(context.Exception,
            "[Orchestration] Dead-lettered phase '{PhaseId}' of scene '{SceneId}' on '{Target}' after {Attempts} attempt(s): {Reason}.",
            context.PhaseId, context.OrchestrationId, context.Target.GetType().Name, context.Attempts, context.Reason);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// In-memory dead-letter queue that retains failed phases for inspection or later replay. Thread-safe.
/// </summary>
public sealed class InMemoryDeadLetterQueue : IDeadLetterSink
{
    private readonly ConcurrentQueue<DeadLetterContext> _queue = new();

    public int Count => _queue.Count;

    public ValueTask HandleAsync(DeadLetterContext context, CancellationToken cancellationToken)
    {
        _queue.Enqueue(context);
        return ValueTask.CompletedTask;
    }

    /// <summary>Attempts to dequeue the next dead-lettered phase.</summary>
    public bool TryDequeue(out DeadLetterContext? context) => _queue.TryDequeue(out context);

    /// <summary>Snapshot of the currently queued dead letters.</summary>
    public IReadOnlyCollection<DeadLetterContext> Snapshot() => _queue.ToArray();
}
