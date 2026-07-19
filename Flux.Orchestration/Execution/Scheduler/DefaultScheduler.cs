using Flux.Orchestration.Diagnostics;
using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Resilience;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Flux.Orchestration.Execution.Scheduler;

/// <summary>
/// Schedules and executes orchestration phases, running parallel-marked phases concurrently and sequential
/// phases in order, with configurable logging, timeouts, and retry policies.
/// </summary>
public class DefaultScheduler : IScheduler
{
    private readonly IEngine _engine;
    private readonly ILogger<DefaultScheduler> _logger;

    /// <summary>
    /// Delay inserted between retry attempts. Defaults to <see cref="TimeSpan.Zero"/> (retry immediately).
    /// Set a positive value to back off between attempts.
    /// </summary>
    /// <remarks>
    /// This is the delay used by the built-in fallback policy. When an explicit <see cref="RetryPolicy"/> is
    /// configured, that policy owns both the retry decision and the delay, and this value is ignored.
    /// </remarks>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Optional retry policy deciding whether a failed attempt is retried and how long to back off.
    /// When null, the scheduler falls back to "retry every failure up to the phase's <c>MaxRetries</c>
    /// with <see cref="RetryDelay"/>" — the historical behaviour.
    /// </summary>
    public IRetryPolicy? RetryPolicy { get; init; }

    /// <summary>
    /// Optional per-phase circuit breaker. When configured, a phase whose circuit is open is short-circuited
    /// (its target is never invoked) and routed to the <see cref="DeadLetterSink"/> — or, if none is set,
    /// surfaced as a <see cref="CircuitOpenException"/>.
    /// </summary>
    public ICircuitBreaker? CircuitBreaker { get; init; }

    /// <summary>
    /// Optional dead-letter sink. When configured, a phase that ultimately fails (retries exhausted) or is
    /// short-circuited by an open circuit is captured here instead of throwing, so a single failing phase no
    /// longer aborts its DAG level / scene. When null, the final failure is thrown to the caller.
    /// </summary>
    public IDeadLetterSink? DeadLetterSink { get; init; }

    // Lazily-built fallback policy that reproduces the legacy "retry everything with RetryDelay" behaviour.
    // Built on first use because RetryDelay is an init property finalized after the constructor runs.
    private IRetryPolicy? _fallbackRetryPolicy;
    private IRetryPolicy EffectiveRetryPolicy =>
        RetryPolicy ?? (_fallbackRetryPolicy ??= new DefaultRetryPolicy(RetryDelay));

    /// <summary>Initializes a new <see cref="DefaultScheduler"/>.</summary>
    /// <param name="engine">The engine used to invoke phase delegates.</param>
    /// <param name="logger">Logger for scheduling diagnostics.</param>
    public DefaultScheduler(IEngine engine, ILogger<DefaultScheduler> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ScheduleAsync(IEnumerable<ScenePhaseManifest> phaseManifests, CancellationToken cancellationToken = default)
    {
        // Index the input directly (List/array both implement IReadOnlyList) to avoid boxing an
        // IEnumerator on the hot path. The ToArray fallback only fires for genuinely lazy sequences,
        // which the planner never produces.
        var items = phaseManifests as IReadOnlyList<ScenePhaseManifest> ?? phaseManifests.ToArray();
        if (items.Count == 0)
            return;

        // Accumulate a contiguous run of parallel manifests into a pooled buffer, then dispatch the whole
        // run at once. The buffer is rented from the shared pool, so this allocates nothing per call.
        ScenePhaseManifest[] parallelBuffer = ArrayPool<ScenePhaseManifest>.Shared.Rent(Math.Max(items.Count, 16));
        int count = 0;

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var manifest = items[i];
                if (manifest.Parallel)
                {
                    if (count >= parallelBuffer.Length)
                    {
                        var grown = ArrayPool<ScenePhaseManifest>.Shared.Rent(parallelBuffer.Length * 2);
                        Array.Copy(parallelBuffer, grown, count);
                        ArrayPool<ScenePhaseManifest>.Shared.Return(parallelBuffer, clearArray: true);
                        parallelBuffer = grown;
                    }
                    parallelBuffer[count++] = manifest;
                }
                else
                {
                    // Flush the pending parallel group BEFORE the sequential phase so ordering holds.
                    if (count > 0)
                    {
                        await RunParallelGroupAsync(parallelBuffer, count, cancellationToken);
                        Array.Clear(parallelBuffer, 0, count);
                        count = 0;
                    }

                    await ExecutePhaseAsync(manifest, cancellationToken);
                }
            }

            if (count > 0)
                await RunParallelGroupAsync(parallelBuffer, count, cancellationToken);
        }
        finally
        {
            ArrayPool<ScenePhaseManifest>.Shared.Return(parallelBuffer, clearArray: true);
        }
    }

    /// <summary>
    /// Runs a group of parallel manifests with bounded, worker-reusing concurrency.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, CancellationToken, Func{TSource, CancellationToken, ValueTask})"/>
    /// so a batch of N items costs O(1) dispatch state instead of N <c>Task.Run</c> allocations — this gives
    /// real parallelism for BOTH synchronous and asynchronous phases while keeping per-tick allocation flat.
    /// A single-item group is run inline to skip the partitioner overhead entirely.
    /// </remarks>
    private async Task RunParallelGroupAsync(ScenePhaseManifest[] buffer, int count, CancellationToken cancellationToken)
    {
        if (count == 1)
        {
            await ExecutePhaseAsync(buffer[0], cancellationToken);
            return;
        }

        await Parallel.ForEachAsync(
            new ArraySegment<ScenePhaseManifest>(buffer, 0, count),
            cancellationToken,
            (manifest, token) => new ValueTask(ExecutePhaseAsync(manifest, token)));
    }


    private Task ExecutePhaseAsync(ScenePhaseManifest manifest, CancellationToken cancellationToken = default)
    {
        // Fast path: no resilience features and no per-phase timeout/retry/logging → invoke directly with the
        // only side-effect being the dispatched counter. Preserves the original zero-overhead hot path.
        if (manifest.Timeout is null && manifest.MaxRetries == 0 && !manifest.Logging
            && RetryPolicy is null && CircuitBreaker is null && DeadLetterSink is null)
        {
            OrchestrationDiagnostics.PhasesDispatched.Add(1);

            ValueTask vt;
            try
            {
                vt = ExecuteInternalAsync(manifest, cancellationToken);
            }
            catch
            {
                // Synchronous failure on the fast path — count it before rethrowing.
                OrchestrationDiagnostics.PhaseFailures.Add(1);
                throw;
            }

            // Success completes synchronously for sync phases → zero allocation, no state machine.
            if (vt.IsCompletedSuccessfully)
                return Task.CompletedTask;

            // Pending or faulted: observe the outcome so failures are counted too. AsTask() preserves the
            // original exception (faulted or cancelled) for the awaiter rather than re-wrapping it.
            return AwaitAndCountFailureAsync(vt);
        }

        return ExecutePhaseHeavyAsync(manifest, cancellationToken);
    }

    /// <summary>Awaits a fast-path phase that did not complete synchronously, counting a failure if it faults.</summary>
    private static async Task AwaitAndCountFailureAsync(ValueTask vt)
    {
        try
        {
            await vt;
        }
        catch
        {
            OrchestrationDiagnostics.PhaseFailures.Add(1);
            throw;
        }
    }

    /// <summary>
    /// Full execution path: circuit-breaker gate → retry loop (policy-driven) with per-attempt timeout →
    /// final disposition (dead-letter or throw). A timeout (the linked CTS firing while the caller's token is
    /// NOT cancelled) is treated as a retryable failure surfaced as a <see cref="TimeoutException"/>; genuine
    /// external cancellation propagates immediately, is never retried, and is never dead-lettered.
    /// </summary>
    private async Task ExecutePhaseHeavyAsync(ScenePhaseManifest manifest, CancellationToken cancellationToken)
    {
        var breaker = CircuitBreaker;
        var sink = DeadLetterSink;
        bool logging = manifest.Logging;

        OrchestrationKey key = default;
        if (breaker is not null)
        {
            key = new OrchestrationKey(manifest.OrchestrationId, manifest.PhaseId);

            // ── Circuit-breaker gate ── short-circuit without ever invoking the target.
            if (breaker.IsOpen(key))
            {
                OrchestrationDiagnostics.CircuitOpened.Add(1);
                var circuitEx = new CircuitOpenException(
                    $"Circuit for phase '{manifest.PhaseId}' of scene '{manifest.OrchestrationId}' is open.");

                if (sink is not null)
                {
                    OrchestrationDiagnostics.PhasesDeadLettered.Add(1);
                    await sink.HandleAsync(
                        new DeadLetterContext(manifest.OrchestrationId, manifest.PhaseId, manifest.Target,
                            Attempts: 0, circuitEx, DeadLetterReason.CircuitOpen),
                        cancellationToken);
                    return;
                }

                if (logging)
                    _logger.LogWarning("[Orchestration] Phase '{PhaseId}' short-circuited by open circuit.", manifest.PhaseId);
                throw circuitEx;
            }
        }

        OrchestrationDiagnostics.PhasesDispatched.Add(1);

        var policy = EffectiveRetryPolicy;
        var timeout = manifest.Timeout;
        var maxRetries = manifest.MaxRetries;
        Exception? lastException = null;
        int attempt = 0;

        while (true)
        {
            try
            {
                if (attempt > 0)
                {
                    OrchestrationDiagnostics.PhaseRetries.Add(1);
                    if (logging)
                        _logger.LogInformation("[Orchestration] Retrying phase '{PhaseId}' on '{Target}', attempt {Attempt}.",
                            manifest.PhaseId, manifest.Target.GetType().Name, attempt + 1);
                }

                if (timeout is { } timeoutApplicable)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(timeoutApplicable);
                    await ExecuteInternalAsync(manifest, cts.Token);
                }
                else
                {
                    await ExecuteInternalAsync(manifest, cancellationToken);
                }

                // ── Success ──
                breaker?.RecordSuccess(key);
                if (logging && attempt > 0)
                    _logger.LogInformation("[Orchestration] Phase '{PhaseId}' recovered on attempt {Attempt}.",
                        manifest.PhaseId, attempt + 1);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // external cancellation — not a timeout, never retried, never dead-lettered
            }
            catch (OperationCanceledException)
            {
                // The linked CTS fired but the caller's token did not → this was our timeout.
                OrchestrationDiagnostics.PhaseTimeouts.Add(1);
                lastException = new TimeoutException($"Phase '{manifest.PhaseId}' timed out after {timeout}.");
                if (logging)
                    _logger.LogWarning(lastException, "[Orchestration] Phase '{PhaseId}' timed out on attempt {Attempt}.",
                        manifest.PhaseId, attempt + 1);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (logging)
                    _logger.LogWarning(ex, "[Orchestration] Phase '{PhaseId}' failed on attempt {Attempt}.",
                        manifest.PhaseId, attempt + 1);
            }

            // ── Retry decision is owned by the policy ──
            if (!policy.ShouldRetry(lastException, attempt, maxRetries))
                break;

            var delay = policy.GetRetryDelay(attempt);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            attempt++;
        }

        // ── Final failure ──
        breaker?.RecordFailure(key);
        OrchestrationDiagnostics.PhaseFailures.Add(1);

        int attempts = attempt + 1;
        if (sink is not null)
        {
            OrchestrationDiagnostics.PhasesDeadLettered.Add(1);
            await sink.HandleAsync(
                new DeadLetterContext(manifest.OrchestrationId, manifest.PhaseId, manifest.Target,
                    attempts, lastException!, DeadLetterReason.RetriesExhausted),
                cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Phase '{manifest.PhaseId}' failed after {attempts} attempt(s).", lastException);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask ExecuteInternalAsync(ScenePhaseManifest manifest, CancellationToken token)
    {
        return manifest.MethodBindingInfo.Signature switch
        {
            MethodCallSignature.Sync => InvokeSyncDirect(manifest, token),
            MethodCallSignature.ValueTask => _engine.InvokeValueTaskAsync(manifest.Target, manifest.MethodBindingInfo.ValueTaskDelegate!, manifest.Context, token),
            MethodCallSignature.Task => new ValueTask(_engine.InvokeTaskAsync(manifest.Target, manifest.MethodBindingInfo.TaskDelegate!, manifest.Context, token)),
            _ => throw new InvalidOperationException()
        };
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask InvokeSyncDirect(ScenePhaseManifest manifest, CancellationToken token)
    {
        _engine.InvokeSync(manifest.Target, manifest.MethodBindingInfo.SyncDelegate!, manifest.Context, token);
        return ValueTask.CompletedTask;
    }
}
