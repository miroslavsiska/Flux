using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Resilience;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Execution.Resilience;

/// <summary>
/// Tests for the failure-policy layer: retry policies, circuit breaker, and dead-lettering.
/// </summary>
public class ResilienceTests
{
    private static ScenePhaseManifest Manifest(Action body, int maxRetries = 0, string sceneId = "S")
    {
        var scene = new SceneMetadata(sceneId, null, [], logging: false);
        var phase = new ScenePhaseMetadata("P", logging: false, maxRetries: maxRetries);
        var binding = new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => body());
        var target = new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding);
        return new ScenePhaseManifest(scene, phase, target, new SceneContext());
    }

    // ── ExponentialBackoffRetryPolicy ────────────────────────────────────────

    [Fact]
    public void ExponentialBackoff_GrowsAndCaps()
    {
        var p = new ExponentialBackoffRetryPolicy(
            TimeSpan.FromMilliseconds(10), maxDelay: TimeSpan.FromMilliseconds(100), factor: 2, jitter: false);

        Assert.Equal(10, p.GetRetryDelay(0).TotalMilliseconds, 3);
        Assert.Equal(20, p.GetRetryDelay(1).TotalMilliseconds, 3);
        Assert.Equal(40, p.GetRetryDelay(2).TotalMilliseconds, 3);
        Assert.Equal(100, p.GetRetryDelay(10).TotalMilliseconds, 3); // capped
        Assert.True(p.ShouldRetry(new Exception(), attempt: 0, maxRetries: 3));
        Assert.False(p.ShouldRetry(new Exception(), attempt: 3, maxRetries: 3));
    }

    // ── CircuitBreaker (unit) ────────────────────────────────────────────────

    [Fact]
    public void CircuitBreaker_OpensAfterThreshold_AndClosesOnSuccess()
    {
        var cb = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMinutes(1));
        var key = new OrchestrationKey("s", "p");

        Assert.False(cb.IsOpen(key));
        cb.RecordFailure(key);
        Assert.False(cb.IsOpen(key)); // 1 < 2
        cb.RecordFailure(key);
        Assert.True(cb.IsOpen(key));  // threshold reached → open

        cb.RecordSuccess(key);
        Assert.False(cb.IsOpen(key)); // closed again
    }

    // ── Retry predicate ──────────────────────────────────────────────────────

    [Fact]
    public async Task FatalException_IsNotRetried()
    {
        int invocations = 0;
        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance)
        {
            RetryPolicy = new DefaultRetryPolicy(isFatal: ex => ex is ArgumentException)
        };

        var manifest = Manifest(() => { invocations++; throw new ArgumentException("fatal"); }, maxRetries: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ScheduleAsync([manifest]));
        Assert.Equal(1, invocations); // no retries despite maxRetries: 3
    }

    // ── Dead-letter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeadLetterSink_CapturesFinalFailure_WithoutThrowing()
    {
        var dlq = new InMemoryDeadLetterQueue();
        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance)
        {
            DeadLetterSink = dlq
        };

        var manifest = Manifest(() => throw new InvalidOperationException("boom"), maxRetries: 0);

        // Does not throw — the failure is captured.
        await scheduler.ScheduleAsync([manifest]);

        Assert.Equal(1, dlq.Count);
        var letter = dlq.Snapshot().First();
        Assert.Equal(DeadLetterReason.RetriesExhausted, letter.Reason);
        Assert.Equal("P", letter.PhaseId);
    }

    // ── Circuit breaker + dead-letter (via scheduler) ────────────────────────

    [Fact]
    public async Task OpenCircuit_ShortCircuits_AndDeadLetters()
    {
        var dlq = new InMemoryDeadLetterQueue();
        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance)
        {
            CircuitBreaker = new CircuitBreaker(failureThreshold: 1, openDuration: TimeSpan.FromMinutes(1)),
            DeadLetterSink = dlq
        };

        int invocations = 0;
        ScenePhaseManifest Make() => Manifest(() => { invocations++; throw new InvalidOperationException("boom"); });

        // First call fails and opens the circuit (threshold 1) → dead-lettered (RetriesExhausted).
        await scheduler.ScheduleAsync([Make()]);
        // Second call is short-circuited before invoking the target → dead-lettered (CircuitOpen).
        await scheduler.ScheduleAsync([Make()]);

        Assert.Equal(1, invocations); // target invoked only once; second call short-circuited
        Assert.Equal(2, dlq.Count);
        var letters = dlq.Snapshot().ToList();
        Assert.Equal(DeadLetterReason.RetriesExhausted, letters[0].Reason);
        Assert.Equal(DeadLetterReason.CircuitOpen, letters[1].Reason);
    }
}
