using Flux.Orchestration.Diagnostics;
using Flux.Orchestration.Durability;
using Flux.Orchestration.Execution;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Flux.Orchestration.Resources;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// Provides a default implementation of <see cref="IPlanner"/> and <see cref="IOrchestrationLifetime"/>,
/// responsible for planning and scheduling orchestrations based on their defined phases and priorities.
/// </summary>
public class DefaultPlanner : IPlanner, IOrchestrationLifetime, IDisposable
{
    private readonly ISceneMetadataRegistry _metadataRegistry;
    private readonly ITargetRegistry _targetRegistry;
    private readonly IScheduler _scheduler;
    private readonly ILogger<DefaultPlanner> _logger;

    // ── Durability (optional) ─────────────────────────────────────────────────
    // Coalesced to no-op implementations so the hot path never null-checks; the *enabled* flags gate the
    // (allocating) journal/checkpoint work so the default wiring stays zero-overhead.
    private readonly IOrchestrationJournal _journal;
    private readonly ISceneStateStore _store;
    private readonly IStateSerializer? _serializer;
    private readonly bool _journalEnabled;
    private readonly bool _durabilityEnabled;
    private readonly bool _resourceJournalingEnabled;

    // ── Lifecycle state ──────────────────────────────────────────────────────
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private int _loopStarted;   // 0 = not started, 1 = started — Interlocked guard
    private int _disposed;      // 0 = alive, 1 = disposed — Interlocked guard
    private int _ticking;       // 0 = idle, 1 = a Tick is in flight — guards loop vs external Tick

    // ── Active scenes ────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, SceneRuntimeState> _activeScenes = new();

    // Optional per-scene thread-affinity: sceneId → dispatcher. Lazily created; null when no scene is pinned,
    // so the default (free-threaded) dispatch path allocates nothing.
    private ConcurrentDictionary<string, IExecutionDispatcher>? _affinity;

    // Priority-sorted snapshot used by Tick() to process scenes in deterministic order.
    // Rebuilt lazily when _scenesDirty is true.
    private SceneRuntimeState[] _sortedScenes = [];
    private volatile bool _scenesDirty = true;

    // ── Defaults ─────────────────────────────────────────────────────────────
    private static readonly TimeSpan DefaultAggregateInterval  = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DefaultFixedStepInterval  = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan DefaultTransitionInterval = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Fallback planning interval used by the interval-based modes (Aggregate / FixedTimestep / Transition)
    /// when neither the scene nor the per-mode default specifies one. Null leaves the per-mode defaults in effect.
    /// </summary>
    public TimeSpan? DefaultPlanningInterval { get; init; }
    public ScenePlanningMode DefaultPlanningMode { get; init; } = ScenePlanningMode.SnapshotDriven;

    /// <summary>
    /// Period of the internal background loop started by <see cref="Start"/>. Defaults to 1 ms.
    /// </summary>
    /// <remarks>
    /// NOTE: On Windows the system timer resolution is ~15.6 ms unless it has been raised
    /// (e.g. via <c>timeBeginPeriod</c>), so the effective tick rate is capped accordingly and the
    /// FixedTimestep / Transition intervals will not be honoured precisely under the default. For
    /// high-precision pacing, either raise the OS timer resolution or drive <see cref="Tick"/> externally
    /// from a vsync-aligned source (e.g. requestAnimationFrame on the web renderer side).
    /// </remarks>
    public TimeSpan LoopInterval { get; init; } = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// How the planner reacts when a scene is asked to plan while its previous plan is still running.
    /// Defaults to <see cref="BackpressurePolicy.DropNewest"/> (the historical skip-and-coalesce behaviour).
    /// </summary>
    public BackpressurePolicy Backpressure { get; init; } = BackpressurePolicy.DropNewest;

    /// <summary>
    /// When the planner persists scene state to the configured store. Defaults to
    /// <see cref="CheckpointPolicy.EveryPlan"/> (checkpoint after each successful plan).
    /// </summary>
    public CheckpointPolicy Checkpoint { get; init; } = CheckpointPolicy.EveryPlan;

    /// <summary>
    /// Minimum logical-time gap between checkpoints of the same scene under <see cref="CheckpointPolicy.Periodic"/>.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(5);

    // Live count of scenes whose plan is executing right now (this planner instance) — for the Load snapshot.
    private int _inFlight;

    /// <summary>A point-in-time view of planner load, for adaptive hosts that throttle input under pressure.</summary>
    public OrchestrationLoad Load => new(_activeScenes.Count, Volatile.Read(ref _inFlight));

    /// <summary>
    /// When true (and journaling is enabled), every <see cref="Tick"/> appends a <c>Tick</c> event recording its
    /// delta and logical time, so an externally-driven planner can be replayed with identical timing. Off by
    /// default: the internal high-frequency loop would otherwise flood the journal. Intended for hosts that drive
    /// <see cref="Tick"/> themselves (the recommended deterministic-replay setup).
    /// </summary>
    public bool JournalTicks { get; init; }

    // Monotonic logical clock (seconds), advanced by every Tick's delta. Stamped onto journaled events so a
    // replay can reconstruct ordering and timing without depending on wall-clock.
    private double _logicalTimeSeconds;

    // Idempotency dedupe: keys seen by this planner, bounded FIFO so a long-running process can't grow forever.
    private const int MaxSeenKeys = 8192;
    private readonly ConcurrentDictionary<string, byte> _seenKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _seenKeyOrder = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPlanner"/> class.
    /// </summary>
    /// <param name="metadataRegistry">Resolves scenes by id and by signal.</param>
    /// <param name="targetRegistry">Resolves the targets bound to a scene/phase.</param>
    /// <param name="scheduler">Executes the per-level manifests produced by planning.</param>
    /// <param name="logger">Diagnostics logger.</param>
    /// <param name="journal">
    /// Optional append-only journal of orchestration inputs/decisions (signals received, scenes planned/skipped).
    /// When null, journaling is disabled with zero overhead.
    /// </param>
    /// <param name="store">
    /// Optional durable scene-state store. When provided, the planner checkpoints a scene's context on each
    /// plan and can rehydrate scenes via <see cref="RestoreAsync"/> after a restart. When null, durability is
    /// disabled with zero overhead.
    /// </param>
    /// <param name="serializer">
    /// Optional state serializer. When provided together with <paramref name="journal"/>, every write to a scene's
    /// <c>Resources</c> store is journaled as a <see cref="OrchestrationEventKind.ResourceWritten"/> event with the
    /// serialized value, enabling value-level event-sourcing: a replay can reconstruct resource state from the log
    /// without re-running side-effecting targets. When null, resource writes are not journaled (zero overhead).
    /// </param>
    public DefaultPlanner(
        ISceneMetadataRegistry metadataRegistry,
        ITargetRegistry targetRegistry,
        IScheduler scheduler,
        ILogger<DefaultPlanner> logger,
        IOrchestrationJournal? journal = null,
        ISceneStateStore? store = null,
        IStateSerializer? serializer = null)
    {
        _metadataRegistry = metadataRegistry;
        _targetRegistry   = targetRegistry;
        _scheduler        = scheduler;
        _logger           = logger;

        _journal          = journal ?? NullOrchestrationJournal.Instance;
        _store            = store   ?? NullSceneStateStore.Instance;
        _serializer       = serializer;
        _journalEnabled   = journal is not null;
        _durabilityEnabled = store is not null;
        _resourceJournalingEnabled = journal is not null && serializer is not null;
    }

    /// <inheritdoc/>
    public Task PlanSceneAsync(string sceneId, SceneContext context, CancellationToken cancellationToken = default)
        => PlanSceneAsync(sceneId, context, idempotencyKey: null, cancellationToken);

    /// <summary>
    /// Plans a scene with an idempotency key. A duplicate key (already seen by this planner) is ignored, so the
    /// same logical command applied more than once — e.g. a retried request or a replayed journal — takes effect
    /// at most once.
    /// </summary>
    public Task PlanSceneAsync(string sceneId, SceneContext context, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        var scene = _metadataRegistry.Resolve(sceneId)
            ?? throw new InvalidOperationException($"Orchestration scene with ID '{sceneId}' is not registered.");
        return PlanSceneAsync(scene, context, idempotencyKey, cancellationToken);
    }

    /// <inheritdoc/>
    public Task PlanSceneAsync(SceneMetadata scene, SceneContext context, CancellationToken cancellationToken = default)
        => PlanSceneAsync(scene, context, idempotencyKey: null, cancellationToken);

    /// <summary>Plans a scene with an idempotency key (see the string-id overload).</summary>
    public Task PlanSceneAsync(SceneMetadata scene, SceneContext context, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!MarkSeen(idempotencyKey))
            return Task.CompletedTask;   // duplicate command — applied at most once

        RegisterPending(scene, context);

        if (!_journalEnabled)
            return Task.CompletedTask;

        return _journal.AppendAsync(
            new JournalEvent(OrchestrationEventKind.ScenePlanRequested, scene.Id, null, context.CorrelationId, null,
                IdempotencyKey: idempotencyKey, LogicalTimeSeconds: Volatile.Read(ref _logicalTimeSeconds)),
            cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public Task PlanSignalAsync(string signal, SceneContext context, CancellationToken cancellationToken = default)
        => PlanSignalAsync(signal, context, idempotencyKey: null, cancellationToken);

    /// <summary>Dispatches a signal with an idempotency key; a duplicate key is ignored (applied at most once).</summary>
    public Task PlanSignalAsync(string signal, SceneContext context, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!MarkSeen(idempotencyKey))
            return Task.CompletedTask;

        var scenes = _metadataRegistry.ResolveBySignal(signal)
            ?? throw new InvalidOperationException($"No orchestration scene is registered with signal ID '{signal}'.");

        // When journaling is off, stay fully synchronous (no state machine, no allocations).
        if (!_journalEnabled)
        {
            foreach (var scene in scenes)
                RegisterPending(scene, context);
            return Task.CompletedTask;
        }

        return PlanSignalJournaledAsync(signal, scenes, context, idempotencyKey, cancellationToken);
    }

    private async Task PlanSignalJournaledAsync(
        string signal, IEnumerable<SceneMetadata> scenes, SceneContext context, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var logicalTime = Volatile.Read(ref _logicalTimeSeconds);
        foreach (var scene in scenes)
        {
            RegisterPending(scene, context);
            await _journal.AppendAsync(
                new JournalEvent(OrchestrationEventKind.SignalReceived, scene.Id, null, context.CorrelationId, signal,
                    IdempotencyKey: idempotencyKey, LogicalTimeSeconds: logicalTime),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<SceneExecutionResult> ExecuteSceneAsync(string sceneId, SceneContext context, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var scene = _metadataRegistry.Resolve(sceneId)
            ?? throw new InvalidOperationException($"Orchestration scene with ID '{sceneId}' is not registered.");
        return ExecuteSceneAsync(scene, context, dryRun, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SceneExecutionResult> ExecuteSceneAsync(SceneMetadata scene, SceneContext context, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(context);

        // Compile the phase DAG (same preflight compiler as the tick-driven path; throws on cycles / unknown deps).
        var sortedPhases = scene.Phases.OrderBy(p => p.Priority).ToList();
        var plan = SceneExecutionPlan.Compile(sortedPhases, scene.Id);

        var phasesWithWork = 0;
        var totalTargets = 0;

        // Walk the levels in order; await each before the next (the await IS the inter-level barrier — phases on the
        // same level fan out per the scheduler). DIRECT: no _activeScenes entry and no Tick, so there is no per-scene-id
        // state to collide on — the same scene id can run concurrently / recurse, and this task completes only when
        // every phase has run. Manifests are built fresh per level (this is not the per-tick hot path, so no pool).
        foreach (var level in plan.Levels)
        {
            var manifests = new List<ScenePhaseManifest>(level.Count);
            foreach (var phase in level)
            {
                var targets = _targetRegistry.ResolveMetadata(new OrchestrationKey(scene.Id, phase.PhaseId));
                if (targets.Count == 0)
                    continue;
                phasesWithWork++;
                for (int t = 0; t < targets.Count; t++)
                    manifests.Add(new ScenePhaseManifest(scene, phase, targets[t], context));
            }

            totalTargets += manifests.Count;
            if (manifests.Count == 0)
                continue;

            // dry-run = imagination: walk + count what WOULD run, but invoke nothing (zero side effects).
            if (!dryRun)
                await _scheduler.ScheduleAsync(manifests, cancellationToken).ConfigureAwait(false);
        }

        return new SceneExecutionResult(plan.Levels.Count, phasesWithWork, totalTargets, dryRun);
    }

    /// <summary>
    /// Records an idempotency key as seen. Returns true if the command should proceed (no key, or first time the
    /// key is seen); false if it is a duplicate. The seen-set is bounded to avoid unbounded growth.
    /// </summary>
    private bool MarkSeen(string? idempotencyKey)
    {
        if (idempotencyKey is null)
            return true;
        if (!_seenKeys.TryAdd(idempotencyKey, 0))
            return false;
        _seenKeyOrder.Enqueue(idempotencyKey);
        while (_seenKeyOrder.Count > MaxSeenKeys && _seenKeyOrder.TryDequeue(out var evicted))
            _seenKeys.TryRemove(evicted, out _);
        return true;
    }

    private void RegisterPending(SceneMetadata scene, SceneContext context)
    {
        var state = EnsureSceneState(scene);
        state.Context = context;
        state.PendingInvalidation = true;
        HookResourceTracking(state, context);
    }

    /// <summary>
    /// Installs an <see cref="ResourceStore.OnWrite"/> hook on the scene's context. Every resource write marks the
    /// scene dirty (so durability can flush it, including in-place mutations made outside a plan) and, when
    /// resource journaling is enabled, appends a <see cref="OrchestrationEventKind.ResourceWritten"/> event with the
    /// serialized value (so a replay can reconstruct resource state from the log alone). No-op when durability and
    /// journaling are both off.
    /// </summary>
    private void HookResourceTracking(SceneRuntimeState state, SceneContext context)
    {
        if (!_durabilityEnabled && !_resourceJournalingEnabled)
            return;
        if (context.Resources is not ResourceStore store)
            return;

        var sceneId = state.Scene.Id;
        var correlationId = context.CorrelationId;
        store.OnWrite = (name, version, value) =>
        {
            state.Dirty = true;
            if (_resourceJournalingEnabled)
                AppendResourceWrite(sceneId, correlationId, name, version, value);
        };
    }

    private void AppendResourceWrite(string sceneId, Guid correlationId, string name, long version, object? value)
    {
        // Serialize the single value through the configured serializer (wrapped in a one-entry bag). A null
        // value is recorded with a null payload and rehydrated as null on replay.
        var payload = value is null
            ? null
            : _serializer!.Serialize(new Dictionary<string, object> { ["v"] = value });

        _ = _journal.AppendAsync(
            new JournalEvent(OrchestrationEventKind.ResourceWritten, sceneId, null, correlationId, name,
                Payload: payload, LogicalTimeSeconds: Volatile.Read(ref _logicalTimeSeconds), Version: version))
            .AsTask();
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _loopStarted, 1, 0) != 0)
            return;
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync()
    {
        _cts.Cancel();   // cancels the loop AND (via linked tokens) every scene's in-flight work
        if (_loopTask is { } t)
            await t.ConfigureAwait(false);

        // Drain in-flight scene plans so callers know all work has unwound after StopAsync returns.
        foreach (var state in _activeScenes.Values)
            await DrainAsync(state).ConfigureAwait(false);

        // Flush any unpersisted state so a graceful stop loses nothing (no-op without durability).
        if (_durabilityEnabled)
            await CheckpointAllAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        // PeriodicTimer fires at the requested period without drift accumulation.
        // Actual resolution depends on the OS scheduler (see LoopInterval remarks).
        using var timer = new PeriodicTimer(LoopInterval);
        var sw = Stopwatch.StartNew();

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var delta = sw.Elapsed;
                sw.Restart();
                Tick(delta, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // NOOP - Normal shutdown path.
        }
    }

    /// <inheritdoc/>
    public void Tick(TimeSpan delta, CancellationToken cancellationToken = default)
    {
        // Reentrancy guard: the internal loop and an external (game-loop) caller must not run a tick
        // concurrently — they share _sortedScenes and per-scene accumulators. If a tick is already in
        // flight, skip this one rather than corrupting that state.
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
        {
            OrchestrationDiagnostics.TicksDropped.Add(1);
            return;
        }

        OrchestrationDiagnostics.Ticks.Add(1);
        var tickStart = Stopwatch.GetTimestamp();

        // Advance the logical clock and (optionally) journal the tick so timing is reproducible on replay.
        var logicalTime = _logicalTimeSeconds + delta.TotalSeconds;
        _logicalTimeSeconds = logicalTime;
        if (_journalEnabled && JournalTicks)
            _ = _journal.AppendAsync(
                new JournalEvent(OrchestrationEventKind.Tick, string.Empty, null, Guid.Empty, null,
                    LogicalTimeSeconds: logicalTime, TickDeltaSeconds: delta.TotalSeconds),
                cancellationToken).AsTask();

        try
        {
            // Rebuild sorted snapshot only when the scene set has changed.
            if (_scenesDirty)
            {
                _sortedScenes = [.. _activeScenes.Values.OrderBy(s => s.Scene.Priority)];
                _scenesDirty = false;
            }

            foreach (var state in _sortedScenes)
            {
                switch (state.Mode)
                {
                    case ScenePlanningMode.SnapshotDriven:
                        ProcessSnapshotDriven(state, cancellationToken);
                        break;

                    case ScenePlanningMode.Aggregate:
                        ProcessAggregate(state, delta, cancellationToken);
                        break;

                    case ScenePlanningMode.Immediate:
                        ProcessImmediate(state, cancellationToken);
                        break;

                    case ScenePlanningMode.FixedTimestep:
                        ProcessFixedTimestep(state, delta, cancellationToken);
                        break;

                    case ScenePlanningMode.Transition:
                        ProcessTransition(state, delta, cancellationToken);
                        break;

                    case ScenePlanningMode.Manual:
                        // Driven exclusively by PlanSceneAsync / PlanSignalAsync.
                        break;
                }
            }

            // Periodic policy: flush dirty scenes on cadence, catching in-place mutations without a replan.
            if (_durabilityEnabled && Checkpoint == CheckpointPolicy.Periodic)
                FlushDirtyScenes(logicalTime, cancellationToken);
        }
        finally
        {
            OrchestrationDiagnostics.TickDuration.Record(Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds);
            Interlocked.Exchange(ref _ticking, 0);
        }
    }


    // ═══════════════════════════════════════════════════════════════════════
    // Internal — per-mode processing
    // ═══════════════════════════════════════════════════════════════════════

    private void ProcessSnapshotDriven(SceneRuntimeState state, CancellationToken token)
    {
        if (!state.PendingInvalidation) return;
        state.InFlight = PlanAndExecuteAsync(state, token);
    }

    private void ProcessAggregate(SceneRuntimeState state, TimeSpan delta, CancellationToken token)
    {
        state.Accumulator += delta;
        var interval = state.Scene.PlanningInterval ?? DefaultPlanningInterval ?? DefaultAggregateInterval;
        if (!state.PendingInvalidation || state.Accumulator < interval) return;
        state.Accumulator = TimeSpan.Zero;
        state.InFlight = PlanAndExecuteAsync(state, token);
    }

    private void ProcessImmediate(SceneRuntimeState state, CancellationToken token)
    {
        // Fires every tick regardless of invalidation.
        // The overlap guard inside PlanAndExecuteAsync prevents concurrent executions.
        state.InFlight = PlanAndExecuteAsync(state, token);
    }

    private void ProcessFixedTimestep(SceneRuntimeState state, TimeSpan delta, CancellationToken token)
    {
        state.Accumulator += delta;
        var interval = state.Scene.PlanningInterval ?? DefaultPlanningInterval ?? DefaultFixedStepInterval;

        // Consume all accumulated intervals to keep the fixed-step clock accurate,
        // but schedule only once per tick — the overlap guard prevents queue build-up.
        bool shouldSchedule = false;
        while (state.Accumulator >= interval)
        {
            state.Accumulator -= interval;
            shouldSchedule = true;
        }

        if (shouldSchedule && state.PendingInvalidation)
            state.InFlight = PlanAndExecuteAsync(state, token);
    }

    private void ProcessTransition(SceneRuntimeState state, TimeSpan delta, CancellationToken token)
    {
        state.Accumulator += delta;
        var interval = state.Scene.PlanningInterval ?? DefaultPlanningInterval ?? DefaultTransitionInterval;
        if (!state.PendingInvalidation || state.Accumulator < interval) return;
        state.Accumulator = TimeSpan.Zero;
        // PendingInvalidation is reset inside PlanAndExecuteAsync (before scheduling).
        state.InFlight = PlanAndExecuteAsync(state, token);
    }


    private SceneRuntimeState EnsureSceneState(SceneMetadata scene)
    {
        if (_activeScenes.TryGetValue(scene.Id, out var existing))
            return existing;

        var mode  = scene.ScenePlanningMode ?? DefaultPlanningMode;
        // Link the scene's cancellation source to the planner lifetime so a stop cancels its in-flight work.
        var state = new SceneRuntimeState(scene, mode, _cts.Token);
        if (_activeScenes.TryAdd(scene.Id, state))
            _scenesDirty = true;    // signal Tick() to rebuild sorted snapshot
        else
            _activeScenes.TryGetValue(scene.Id, out state);   // another thread won the race
        return state!;
    }

    private async Task PlanAndExecuteAsync(SceneRuntimeState state, CancellationToken token)
    {
        // Lock-free guard: only one concurrent execution per scene. A skipped tick is the key overload signal,
        // and the point at which the backpressure policy decides what to do with the overlap.
        if (!state.TryBeginProcessing())
        {
            _logger.LogDebug("Scene {Id} is still processing. Skipping this tick.", state.Scene.Id);
            OrchestrationDiagnostics.ScenesSkippedBusy.Add(1);

            if (Backpressure == BackpressurePolicy.DropOldest)
            {
                // Abort the in-flight (oldest) plan and re-arm so the newest state replans on the next tick.
                state.CancelAndReset(_cts.Token);
                state.PendingInvalidation = true;
            }

            if (_journalEnabled)
                await _journal.AppendAsync(
                    new JournalEvent(OrchestrationEventKind.SceneSkippedBusy, state.Scene.Id, null,
                        state.Context?.CorrelationId ?? Guid.Empty, null),
                    token).ConfigureAwait(false);
            return;
        }

        OrchestrationDiagnostics.IncInFlight();
        Interlocked.Increment(ref _inFlight);
        var planStart = Stopwatch.GetTimestamp();
        try
        {
            var context = state.Context;
            if (context is null)
            {
                _logger.LogWarning("Scene {Id} has no context yet. Skipping.", state.Scene.Id);
                return;
            }

            // Reset invalidation BEFORE building manifests so any signal arriving during
            // scheduling sets it to true again and is picked up on the next tick.
            state.PendingInvalidation = false;

            // Use the per-scene cancellation token (linked to the planner lifetime) so RemoveSceneAsync /
            // StopAsync / drop-oldest backpressure can abort this scene's in-flight work.
            var ct = state.CancellationToken;

            // Optional thread-affinity: when the scene is pinned, every level's dispatch runs on its dispatcher.
            IExecutionDispatcher? dispatcher = null;
            _affinity?.TryGetValue(state.Scene.Id, out dispatcher);

            // Walk the compiled DAG level by level. Each level is dispatched to the scheduler and
            // awaited before the next begins — the await IS the inter-level barrier. Phases on the
            // same level may run concurrently; their targets fan out per the manifest's Parallel flag.
            var levels = state.Plan.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                FillManifestBuffer(state, context, levels[i]);

                if (state.ManifestBuffer.Count == 0)
                    continue;

                if (dispatcher is null)
                    await _scheduler.ScheduleAsync(state.ManifestBuffer, ct);
                else
                    await dispatcher.InvokeAsync(() => new ValueTask(_scheduler.ScheduleAsync(state.ManifestBuffer, ct)), ct);
            }

            OrchestrationDiagnostics.ScenesPlanned.Add(1);

            // Audit the decision and checkpoint the scene's state so it can resume after a restart.
            if (_journalEnabled)
                await _journal.AppendAsync(
                    new JournalEvent(OrchestrationEventKind.ScenePlanned, state.Scene.Id, null, context.CorrelationId, null,
                        LogicalTimeSeconds: Volatile.Read(ref _logicalTimeSeconds)),
                    ct).ConfigureAwait(false);

            if (_durabilityEnabled)
            {
                state.Dirty = true;   // a completed plan changed persistable state
                await MaybeCheckpointAsync(state, context, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during planning scene {Id}", state.Scene.Id);
        }
        finally
        {
            OrchestrationDiagnostics.ScenePlanDuration.Record(Stopwatch.GetElapsedTime(planStart).TotalMilliseconds);
            Interlocked.Decrement(ref _inFlight);
            OrchestrationDiagnostics.DecInFlight();
            state.EndProcessing();
        }
    }

    /// <summary>Checkpoints the scene if the current <see cref="Checkpoint"/> policy says it is due.</summary>
    private ValueTask MaybeCheckpointAsync(SceneRuntimeState state, SceneContext context, CancellationToken token)
    {
        bool due = Checkpoint switch
        {
            CheckpointPolicy.EveryPlan => true,
            CheckpointPolicy.Periodic =>
                (Volatile.Read(ref _logicalTimeSeconds) - state.LastCheckpointSeconds) >= CheckpointInterval.TotalSeconds,
            _ => false, // Manual — explicit only
        };
        return due ? FlushCheckpointAsync(state, context, token) : ValueTask.CompletedTask;
    }

    /// <summary>Records the checkpoint bookkeeping (clears Dirty, stamps the time) and persists the snapshot.</summary>
    private ValueTask FlushCheckpointAsync(SceneRuntimeState state, SceneContext context, CancellationToken token)
    {
        // Clear Dirty BEFORE saving so a mutation racing the save re-marks the scene and is flushed next round.
        state.LastCheckpointSeconds = Volatile.Read(ref _logicalTimeSeconds);
        state.Dirty = false;
        return CheckpointAsync(state, context, token);
    }

    /// <summary>
    /// Flushes dirty scenes whose checkpoint interval has elapsed — the periodic policy's tick-driven path. This
    /// persists in-place mutations (e.g. a resource write made outside a plan) without waiting for a replan.
    /// </summary>
    private void FlushDirtyScenes(double logicalTime, CancellationToken token)
    {
        var interval = CheckpointInterval.TotalSeconds;
        foreach (var state in _sortedScenes)
        {
            if (state.Dirty && state.Context is { } ctx && (logicalTime - state.LastCheckpointSeconds) >= interval)
                _ = FlushCheckpointAsync(state, ctx, token).AsTask(); // fire-and-forget; store is latest-wins
        }
    }

    /// <summary>Forces a checkpoint of one active scene now, regardless of policy. No-op without durability.</summary>
    public ValueTask CheckpointSceneAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        if (_durabilityEnabled && _activeScenes.TryGetValue(sceneId, out var state) && state.Context is { } ctx)
            return FlushCheckpointAsync(state, ctx, cancellationToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>Forces a checkpoint of every active scene now, regardless of policy. No-op without durability.</summary>
    public async Task CheckpointAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_durabilityEnabled)
            return;
        foreach (var state in _activeScenes.Values)
            if (state.Context is { } ctx)
                await FlushCheckpointAsync(state, ctx, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists a point-in-time snapshot of the scene's state to the configured <see cref="ISceneStateStore"/>.
    /// Parameters are copied so the snapshot is immune to later mutation of the live context.
    /// </summary>
    private ValueTask CheckpointAsync(SceneRuntimeState state, SceneContext context, CancellationToken token)
    {
        // Snapshot the parameters into a plain dictionary (the live one is a mutable ConcurrentDictionary).
        var parameters = new Dictionary<string, object>(context.Parameters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in context.Parameters)
            parameters[kvp.Key] = kvp.Value;

        // Capture typed resources too, so a restart restores them from the snapshot alone (no journal needed).
        var resources = context.Resources.Snapshot();

        var snapshot = new SceneStateSnapshot(
            state.Scene.Id,
            state.Mode,
            state.PendingInvalidation,
            context.CorrelationId,
            parameters,
            state.NextCheckpointVersion(),
            resources.Count > 0 ? resources : null);

        return _store.SaveAsync(snapshot, token);
    }

    /// <summary>
    /// Rehydrates scenes from the configured <see cref="ISceneStateStore"/> after a restart. Each persisted
    /// snapshot whose scene is still registered is re-registered with its saved context and pending-work flag,
    /// so the next <see cref="Tick"/> resumes it. Returns the number of scenes restored.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while loading snapshots.</param>
    public async Task<int> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!_durabilityEnabled)
            return 0;

        var snapshots = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        int restored = 0;

        foreach (var snapshot in snapshots)
        {
            var scene = _metadataRegistry.Resolve(snapshot.SceneId);
            if (scene is null)
            {
                _logger.LogWarning("Skipping restore of scene {Id}: it is no longer registered.", snapshot.SceneId);
                continue;
            }

            // Reconstruct the context with its original correlation id, parameters, and resources.
            var context = new SceneContext { CorrelationId = snapshot.CorrelationId };
            foreach (var kvp in snapshot.Parameters)
                context.Parameters[kvp.Key] = kvp.Value;
            if (snapshot.Resources is { } resources)
                foreach (var kvp in resources)
                    context.Resources.WriteBoxed(kvp.Key, kvp.Value);

            var state = EnsureSceneState(scene);
            state.Context = context;
            state.PendingInvalidation = snapshot.PendingInvalidation;
            state.LastCheckpointSeconds = Volatile.Read(ref _logicalTimeSeconds);
            HookResourceTracking(state, context);
            restored++;
        }

        _logger.LogInformation("Restored {Count} scene(s) from durable state.", restored);
        return restored;
    }

    /// <summary>
    /// Fills the scene's reusable ManifestBuffer with the manifests for a single execution level,
    /// based on that level's phases and their resolved targets. The manifest record is the only per-tick
    /// heap allocation; tags are computed lazily so an untouched manifest stays cheap.
    /// </summary>
    /// <param name="state">The runtime state of the scene.</param>
    /// <param name="context">The context for the current scene execution.</param>
    /// <param name="level">The phases belonging to the current execution level.</param>
    private void FillManifestBuffer(SceneRuntimeState state, SceneContext context, IReadOnlyList<ScenePhaseMetadata> level)
    {
        // ManifestBuffer and the manifest pool are owned by this state and accessed only under the
        // IsProcessing lock. Both are reused across ticks, so steady-state planning allocates nothing here.
        state.ManifestBuffer.Clear();
        state.ResetManifestRentals();

        foreach (var phase in level)
        {
            var key = new OrchestrationKey(state.Scene.Id, phase.PhaseId);
            var targets = _targetRegistry.ResolveMetadata(key);

            _logger.LogDebug("[Orchestration] Phase '{PhaseId}' — {Count} targets.", phase.PhaseId, targets.Count);

            // ResolveMetadata returns an IReadOnlyList; index it to avoid an enumerator allocation.
            for (int t = 0; t < targets.Count; t++)
                state.ManifestBuffer.Add(state.RentManifest(state.Scene, phase, targets[t], context));
        }
    }

    /// <summary>
    /// Binds a scene to an <see cref="IExecutionDispatcher"/> so all of that scene's phase dispatch runs on the
    /// dispatcher's thread (e.g. a render thread). Pass <see cref="InlineDispatcher.Instance"/> or call
    /// <see cref="UnbindAffinity"/> to remove affinity. The caller owns the dispatcher's lifetime.
    /// </summary>
    public void BindAffinity(string sceneId, IExecutionDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        var map = _affinity ??= new ConcurrentDictionary<string, IExecutionDispatcher>(StringComparer.Ordinal);
        map[sceneId] = dispatcher;
    }

    /// <summary>Removes any thread-affinity binding for the scene. Returns true if one was present.</summary>
    public bool UnbindAffinity(string sceneId) => _affinity?.TryRemove(sceneId, out _) ?? false;

    /// <summary>
    /// Removes an active scene from the planner, stopping further ticking for it and releasing its
    /// runtime state. Cancels the scene's in-flight work but does NOT wait for it to drain — use
    /// <see cref="RemoveSceneAsync"/> when you need to await completion. Returns <see langword="true"/>
    /// if the scene was active and removed.
    /// </summary>
    /// <remarks>
    /// Without this, interval/immediate scenes tick forever and <c>_activeScenes</c> only ever grows.
    /// Call this when a scene's work is done (e.g. a transition has settled) to bound memory and CPU.
    /// </remarks>
    public bool RemoveScene(string sceneId)
    {
        if (_activeScenes.TryRemove(sceneId, out var state))
        {
            state.Cancel();        // signal in-flight work to stop (fire-and-forget; not drained here)
            _scenesDirty = true;   // rebuild the sorted snapshot on the next tick
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes an active scene and awaits its in-flight plan to drain, then disposes its cancellation source.
    /// This is the safe way to retire a scene whose phases have side effects: the returned task completes only
    /// once the running plan has observed cancellation and unwound. Returns <see langword="true"/> if removed.
    /// </summary>
    public async Task<bool> RemoveSceneAsync(string sceneId)
    {
        if (!_activeScenes.TryRemove(sceneId, out var state))
            return false;

        _scenesDirty = true;
        state.Cancel();
        await DrainAsync(state).ConfigureAwait(false);
        state.DisposeCts();
        return true;
    }

    /// <summary>Awaits a scene's most recent plan task, swallowing the expected cancellation on teardown.</summary>
    private static async Task DrainAsync(SceneRuntimeState state)
    {
        if (state.InFlight is { } inFlight)
        {
            try { await inFlight.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch (Exception) { /* the plan loop already logged; draining must not throw */ }
        }
    }

    /// <summary>
    /// Synchronous best-effort teardown for callers that dispose the container synchronously (e.g. <c>using var</c>).
    /// Cancels the planning loop and releases cancellation sources without awaiting in-flight plans to drain; prefer
    /// <see cref="DisposeAsync"/> when running phases must unwind before disposal completes.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        _cts.Cancel();
        foreach (var state in _activeScenes.Values)
            state.DisposeCts();
        _cts.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        _cts.Cancel();

        if (_loopTask is { } t)
        {
            try {
                await t.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // NOOP
            }
        }

        // Drain and release every scene's in-flight work + cancellation source.
        foreach (var state in _activeScenes.Values)
        {
            await DrainAsync(state).ConfigureAwait(false);
            state.DisposeCts();
        }

        _cts.Dispose();
    }
}
