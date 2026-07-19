using Flux.Orchestration.Execution.Planer;

namespace Flux.Orchestration.Durability;

/// <summary>
/// The outcome of a replay: the sequence of scenes the original run planned vs. the sequence reproduced by
/// re-applying the journaled inputs to a fresh planner. <see cref="Verify"/> is the determinism invariant —
/// a faithful replay reproduces exactly the same planning decisions in the same order.
/// </summary>
/// <param name="OriginalPlanned">Scene ids in the order the source journal recorded ScenePlanned.</param>
/// <param name="ReproducedPlanned">Scene ids in the order the replay planned them.</param>
/// <param name="ReconstructedResources">
/// Per-correlation resource state rebuilt purely from journaled <see cref="OrchestrationEventKind.ResourceWritten"/>
/// events — i.e. without executing any target. Empty unless the replayer was given a serializer. This is the
/// value-level event-sourcing result: state recovered from the log alone.
/// </param>
public sealed record ReplayResult(
    IReadOnlyList<string> OriginalPlanned,
    IReadOnlyList<string> ReproducedPlanned,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, object?>> ReconstructedResources)
{
    /// <summary>True when the replay reproduced the original planning-decision sequence exactly.</summary>
    public bool Verify() => OriginalPlanned.SequenceEqual(ReproducedPlanned, StringComparer.Ordinal);

    /// <summary>Reads a reconstructed resource value for a correlation. False if absent.</summary>
    public bool TryGetResource(Guid correlationId, string name, out object? value)
    {
        value = null;
        return ReconstructedResources.TryGetValue(correlationId, out var bag) && bag.TryGetValue(name, out value);
    }
}

/// <summary>
/// Deterministically re-applies a journaled event stream (signals, explicit plan requests, ticks) to a fresh
/// planner and verifies that the same planning decisions are reproduced. This is the determinism/crash-resume
/// proof: given the recorded inputs and tick timing, the orchestrator's decisions are a pure function of them.
/// </summary>
/// <remarks>
/// Inputs are re-applied with their original idempotency keys, so a duplicate command in the source is applied
/// at most once on replay too. Deterministic timing requires the source to have been recorded with
/// <see cref="DefaultPlanner.JournalTicks"/> enabled and ticks driven externally; otherwise interval/timestep
/// decisions cannot be reproduced from the journal alone.
/// <para>
/// Replay reconstructs <em>decisions</em>, not target side effects: the replay planner should be wired with a
/// scheduler that records/no-ops rather than re-running real side-effecting work.
/// </para>
/// </remarks>
public sealed class OrchestrationReplayer
{
    private readonly DefaultPlanner _planner;
    private readonly IOrchestrationJournal _reproducedJournal;
    private readonly IStateSerializer? _serializer;
    private readonly int _settleTimeoutMs;

    /// <param name="planner">A fresh planner to replay into (its scenes must be registered; scheduler should not cause external side effects).</param>
    /// <param name="reproducedJournal">The journal the replay planner writes to, used to collect reproduced decisions. Must be the same instance the planner journals to.</param>
    /// <param name="serializer">Same serializer used when recording, to deserialize journaled resource values for state reconstruction. Null skips resource reconstruction.</param>
    /// <param name="settleTimeoutMs">Max time to wait for fire-and-forget plans to drain after each tick.</param>
    public OrchestrationReplayer(DefaultPlanner planner, IOrchestrationJournal reproducedJournal, IStateSerializer? serializer = null, int settleTimeoutMs = 2000)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _reproducedJournal = reproducedJournal ?? throw new ArgumentNullException(nameof(reproducedJournal));
        _serializer = serializer;
        _settleTimeoutMs = settleTimeoutMs;
    }

    /// <summary>Replays the given source records and returns the comparison of original vs reproduced decisions.</summary>
    public async Task<ReplayResult> ReplayAsync(IReadOnlyList<JournalRecord> source, CancellationToken cancellationToken = default)
    {
        var original = source
            .Where(r => r.Event.Kind == OrchestrationEventKind.ScenePlanned)
            .Select(r => r.Event.SceneId)
            .ToList();

        // Resource state reconstructed purely from the log (no target execution): correlation → name → value.
        var resources = new Dictionary<Guid, Dictionary<string, object?>>();

        foreach (var record in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var e = record.Event;
            switch (e.Kind)
            {
                case OrchestrationEventKind.SignalReceived when e.Detail is { } signal:
                    await _planner.PlanSignalAsync(signal, ContextFrom(e), e.IdempotencyKey, cancellationToken).ConfigureAwait(false);
                    break;

                case OrchestrationEventKind.ScenePlanRequested:
                    await _planner.PlanSceneAsync(e.SceneId, ContextFrom(e), e.IdempotencyKey, cancellationToken).ConfigureAwait(false);
                    break;

                case OrchestrationEventKind.Tick:
                    _planner.Tick(TimeSpan.FromSeconds(e.TickDeltaSeconds), cancellationToken);
                    await SettleAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case OrchestrationEventKind.ResourceWritten when _serializer is not null && e.Detail is { } resourceName:
                    ApplyResourceWrite(resources, e, resourceName);
                    break;

                // ScenePlanned / SceneSkippedBusy are decisions, not inputs — not re-applied.
            }
        }

        await SettleAsync(cancellationToken).ConfigureAwait(false);

        var reproduced = (await _reproducedJournal.ReadAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.Event.Kind == OrchestrationEventKind.ScenePlanned)
            .Select(r => r.Event.SceneId)
            .ToList();

        var reconstructed = resources.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, object?>)kv.Value);

        return new ReplayResult(original, reproduced, reconstructed);
    }

    // Rebuild one resource write from the log: deserialize the value (last-write-wins, journal order).
    private void ApplyResourceWrite(Dictionary<Guid, Dictionary<string, object?>> resources, JournalEvent e, string resourceName)
    {
        object? value = null;
        if (e.Payload is { } payload)
        {
            var bag = _serializer!.Deserialize(payload);
            bag.TryGetValue("v", out value);
        }

        if (!resources.TryGetValue(e.CorrelationId, out var store))
            resources[e.CorrelationId] = store = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        store[resourceName] = value;
    }

    private static SceneContext ContextFrom(JournalEvent e) => new() { CorrelationId = e.CorrelationId };

    // Wait for fire-and-forget plan tasks to drain so reproduced decisions are visible before we read them.
    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + _settleTimeoutMs;
        while (_planner.Load.InFlight > 0 && Environment.TickCount64 < deadline)
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
    }
}
