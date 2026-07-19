using Flux.Orchestration.Model;

namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// Holds the mutable runtime state for an active scene, including the current context, accumulator, and pre-allocated buffers.
/// </summary>
public sealed class SceneRuntimeState
{
    /// <summary>
    /// The static metadata for the scene being executed. Immutable after construction.
    /// </summary>
    public SceneMetadata Scene { get; }

    /// <summary>
    /// The planning mode for this scene execution, which may influence how phases are processed. Immutable after construction.
    /// </summary>
    public ScenePlanningMode Mode { get; }

    /// <summary>
    /// Phases sorted by priority once at construction time — avoids per-tick OrderBy allocation.
    /// </summary>
    public IReadOnlyList<ScenePhaseMetadata> SortedPhases { get; }

    /// <summary>
    /// The compiled DAG execution plan (ordered levels) for this scene. Built once during construction
    /// (preflight) and walked per tick at zero allocation cost.
    /// </summary>
    public SceneExecutionPlan Plan { get; }

    /// <summary>
    /// Pre-allocated, reusable manifest buffer — avoids per-tick List&lt;T&gt; allocation.
    /// Access is safe only while the IsProcessing lock is held (i.e. inside PlanAndExecuteAsync).
    /// </summary>
    public readonly List<ScenePhaseManifest> ManifestBuffer;

    // ── Manifest pool ─────────────────────────────────────────────────────────
    // Pooled, reused ScenePhaseManifest instances. The pool grows to the high-water mark of manifests
    // needed in any single level and is then reused forever — so steady-state planning allocates zero
    // manifests. Safe without locking: only ever touched inside PlanAndExecuteAsync under IsProcessing.
    private readonly List<ScenePhaseManifest> _manifestPool = [];
    private int _manifestsRented;

    /// <summary>Resets the rental cursor so the pooled manifests can be reused for the next level/tick.</summary>
    public void ResetManifestRentals() => _manifestsRented = 0;

    /// <summary>
    /// Rents a manifest from the pool (reusing an existing instance when available, otherwise growing the
    /// pool once). The returned instance must be initialized via <c>Set(...)</c> by the caller.
    /// </summary>
    public ScenePhaseManifest RentManifest(SceneMetadata scene, ScenePhaseMetadata phase, ScenePhaseTargetMetadata target, SceneContext context)
    {
        ScenePhaseManifest manifest;
        if (_manifestsRented < _manifestPool.Count)
        {
            manifest = _manifestPool[_manifestsRented];
            manifest.Set(scene, phase, target, context);
        }
        else
        {
            manifest = new ScenePhaseManifest(scene, phase, target, context);
            _manifestPool.Add(manifest);
        }
        _manifestsRented++;
        return manifest;
    }

    /// <summary>
    /// The context for the current scene execution. May be null until the first PlanSceneAsync/PlanSignalAsync call.
    /// </summary>
    public SceneContext? Context { get; set; }

    /// <summary>
    /// Accumulator for elapsed time between ticks. Used to determine when to advance to the next phase based on timing requirements.
    /// </summary>
    public TimeSpan Accumulator { get; set; }

    // ── In-flight cancellation ────────────────────────────────────────────────
    // Per-scene cancellation source, linked at construction to the planner's lifetime token so that stopping
    // the planner cancels every scene's in-flight work without any per-tick allocation. Cancelling it directly
    // (RemoveSceneAsync / drop-oldest backpressure) cancels just this scene's running plan.
    private CancellationTokenSource _cts;

    /// <summary>The token observed by this scene's in-flight work; cancels on planner stop or explicit scene cancel.</summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>Requests cancellation of this scene's in-flight work (does not dispose the source).</summary>
    public void Cancel() => _cts.Cancel();

    /// <summary>
    /// Cancels the current token source and swaps in a fresh one linked to <paramref name="parentToken"/>.
    /// Used by the drop-oldest backpressure policy: abort the running plan, then let the newest state run on a
    /// clean token. The previous source is disposed.
    /// </summary>
    public void CancelAndReset(CancellationToken parentToken)
    {
        var old = _cts;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        old.Cancel();
        old.Dispose();
    }

    /// <summary>Disposes the scene's cancellation source. Call only after the in-flight task has drained.</summary>
    public void DisposeCts() => _cts.Dispose();

    // The most recently launched plan task, tracked so the planner can await it (RemoveSceneAsync / StopAsync)
    // to drain in-flight work. Read/written across threads via Volatile.
    private Task? _inFlight;
    /// <summary>The most recent fire-and-forget plan task for this scene, or null if none has run.</summary>
    public Task? InFlight
    {
        get => Volatile.Read(ref _inFlight);
        set => Volatile.Write(ref _inFlight, value);
    }

    // volatile ensures cross-thread visibility without a full lock.
    private volatile bool _pendingInvalidation;
    /// <summary>
    /// Indicates whether the scene is pending invalidation due to a signal or other event that occurred during processing.
    /// </summary>
    public bool PendingInvalidation
    {
        get => _pendingInvalidation;
        set => _pendingInvalidation = value;
    }

    // Monotonic checkpoint version, bumped each time the scene's state is persisted. Used by the durable
    // store for latest-wins conflict resolution. Interlocked so concurrent ticks never collide on it.
    private long _checkpointVersion;

    /// <summary>Returns the next monotonic checkpoint version for this scene.</summary>
    public long NextCheckpointVersion() => Interlocked.Increment(ref _checkpointVersion);

    // ── Checkpoint tracking ───────────────────────────────────────────────────
    // Dirty is set whenever the scene's persistable state changes (a completed plan, or an in-place resource
    // write via the store's OnWrite hook) and cleared when checkpointed. Set from target threads, so volatile.
    private volatile bool _dirty;

    /// <summary>Whether the scene has unpersisted state changes since the last checkpoint.</summary>
    public bool Dirty { get => _dirty; set => _dirty = value; }

    /// <summary>Logical time (seconds) of the last checkpoint — used to throttle the Periodic policy.</summary>
    public double LastCheckpointSeconds { get; set; }

    // 0 = idle, 1 = processing. Used with Interlocked.CompareExchange for a lock-free guard.
    private int _isProcessing;

    /// <summary>
    /// Attempts to enter the processing state. Returns true if the caller acquired the lock; false if already processing.
    /// </summary>
    public bool TryBeginProcessing() =>
        Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0;

    /// <summary>
    /// Releases the processing lock so the next tick can schedule work.
    /// </summary>
    public void EndProcessing() =>
        Interlocked.Exchange(ref _isProcessing, 0);

    /// <summary>
    /// Initializes a new instance of the SceneRuntimeState class with the given scene metadata and planning mode.
    /// </summary>
    /// <param name="scene">The metadata for the scene.</param>
    /// <param name="mode">The planning mode for the scene.</param>
    /// <param name="parentToken">
    /// The planner's lifetime token. The scene's cancellation source is linked to it so a planner stop cancels
    /// this scene's in-flight work. Defaults to <see cref="CancellationToken.None"/> (only explicit cancel fires).
    /// </param>
    public SceneRuntimeState(SceneMetadata scene, ScenePlanningMode mode, CancellationToken parentToken = default)
    {
        Scene = scene;
        Mode = mode;
        SortedPhases = [.. scene.Phases.OrderBy(p => p.Priority)];
        // Preflight: compile the dependency graph once. Throws on cycles / unknown dependencies.
        Plan = SceneExecutionPlan.Compile(SortedPhases, scene.Id);
        // Pre-allocate based on the number of phases; avoids most resize operations.
        ManifestBuffer = new List<ScenePhaseManifest>(Math.Max(SortedPhases.Count, 4));
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
    }
}
