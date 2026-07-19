using Flux.Orchestration;
using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using Flux.Orchestration.Resources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Resources;

/// <summary>
/// Pillar 3 — versioned typed resource store: race-free reads, type safety, DAG write-before-read ordering,
/// end-to-end inter-phase sharing, and declared-access validation.
/// </summary>
public class ResourceStoreTests
{
    private sealed record Pair(int A, int B); // invariant under test: A always equals B

    // ── ResourceCell race-freedom ───────────────────────────────────────────────

    [Fact]
    public async Task ResourceCell_ConcurrentReadsAndWrites_NeverTear()
    {
        var cell = new ResourceCell<Pair>(new Pair(0, 0));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writer = Task.Run(() =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested)
                cell.Write(new Pair(i, i++)); // both fields always equal
        });

        // Many readers assert the invariant holds for every value they observe.
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var p = cell.Value;
                Assert.Equal(p.A, p.B); // would fail if a read were torn
            }
        })).ToArray();

        await Task.WhenAll(readers.Append(writer));
        Assert.True(cell.Version > 0);
    }

    [Fact]
    public void ResourceCell_Write_BumpsVersionMonotonically()
    {
        var cell = new ResourceCell<int>(0);
        Assert.Equal(0, cell.Version);
        Assert.Equal(1, cell.Write(10));
        Assert.Equal(2, cell.Write(20));
        var v = cell.Read();
        Assert.Equal(2, v.Version);
        Assert.Equal(20, v.Value);
    }

    // ── ResourceStore basics ───────────────────────────────────────────────────

    [Fact]
    public void ResourceStore_WriteRead_RoundTrips()
    {
        var store = new ResourceStore();
        Assert.False(store.Contains("x"));
        Assert.Equal(0, store.VersionOf("x"));

        var v = store.Write("x", 42);
        Assert.Equal(1, v);
        Assert.True(store.Contains("x"));
        Assert.Equal(42, store.Read<int>("x"));
        Assert.Equal(1, store.VersionOf("x"));
        Assert.Contains("x", store.Names);
    }

    [Fact]
    public void ResourceStore_TryRead_MissingReturnsFalse()
    {
        var store = new ResourceStore();
        Assert.False(store.TryRead<int>("nope", out var value));
        Assert.Equal(0, value);
        Assert.Throws<KeyNotFoundException>(() => store.Read<int>("nope"));
    }

    [Fact]
    public void ResourceStore_TypeMismatch_Throws()
    {
        var store = new ResourceStore();
        store.Write("x", 42);
        Assert.Throws<InvalidOperationException>(() => store.Read<string>("x"));
    }

    [Fact]
    public void ResourceStore_SnapshotAndWriteBoxed_RestoresTypedValues()
    {
        var store = new ResourceStore();
        store.Write("count", 7);
        store.Write("name", "hello");

        var snapshot = store.Snapshot();
        Assert.Equal(7, snapshot["count"]);
        Assert.Equal("hello", snapshot["name"]);

        // Rehydrate into a fresh store via the boxed values — cells must be typed by runtime type,
        // so a subsequent typed read/write matches (no type-mismatch throw).
        var restored = new ResourceStore();
        foreach (var (k, v) in snapshot)
            restored.WriteBoxed(k, v);

        Assert.Equal(7, restored.Read<int>("count"));
        Assert.Equal("hello", restored.Read<string>("name"));
        Assert.Equal(2, restored.Write("count", 8)); // typed cell (no mismatch); WriteBoxed was v1, this is v2
    }

    [Fact]
    public void ResourceStore_OnWriteHook_Fires()
    {
        var store = new ResourceStore();
        (string name, long version, object? value) captured = default;
        store.OnWrite = (n, ver, val) => captured = (n, ver, val);

        store.Write("x", 7);

        Assert.Equal(("x", 1L, (object?)7), captured);
    }

    // ── DAG write-before-read ordering ──────────────────────────────────────────

    [Fact]
    public void Plan_OrdersWriterBeforeReader_ViaReadsWrites()
    {
        var writer = new ScenePhaseMetadata("w") { Writes = ["res"] };
        var reader = new ScenePhaseMetadata("r") { Reads = ["res"] };

        var plan = SceneExecutionPlan.Compile([reader, writer], "s"); // intentionally out of order

        int LevelOf(string id) => Enumerable.Range(0, plan.Levels.Count)
            .First(i => plan.Levels[i].Any(p => p.PhaseId == id));

        Assert.True(LevelOf("w") < LevelOf("r"), "writer must be ordered before reader");
    }

    // ── End-to-end inter-phase sharing through the scheduler ────────────────────

    [Fact]
    public async Task SharedContext_WriterThenReader_ReaderSeesValue()
    {
        var scheduler = new DefaultScheduler(new DefaultEngine(), NullLogger<DefaultScheduler>.Instance);
        var context = new SceneContext();
        int readBack = -1;

        var scene = new SceneMetadata("s", null, []);
        var writePhase = new ScenePhaseMetadata("w");
        var readPhase = new ScenePhaseMetadata("r");

        var writeBinding = new MethodBindingInfo(MethodCallSignature.Sync,
            SyncDelegate: (_, ctx, _) => ctx.Resources.Write("x", 42));
        var readBinding = new MethodBindingInfo(MethodCallSignature.Sync,
            SyncDelegate: (_, ctx, _) => readBack = ctx.Resources.Read<int>("x"));

        ScenePhaseManifest Manifest(ScenePhaseMetadata phase, MethodBindingInfo binding) =>
            new(scene, phase, new ScenePhaseTargetMetadata(new ScenePhaseTarget(new object(), "M"), binding), context);

        // Sequential dispatch (not parallel) preserves order; the reader runs after the writer.
        await scheduler.ScheduleAsync([Manifest(writePhase, writeBinding), Manifest(readPhase, readBinding)]);

        Assert.Equal(42, readBack);
    }

    // ── Declared-access validation ──────────────────────────────────────────────

    [Fact]
    public void Validator_DetectsDanglingRead()
    {
        var reader = new ScenePhaseMetadata("r") { Reads = ["ghost"] };
        var issues = ResourceAccessValidator.Validate([reader]);
        Assert.Single(issues);
        Assert.Equal("ghost", issues[0].Resource);
    }

    [Fact]
    public void Validator_CleanGraph_NoIssues()
    {
        var writer = new ScenePhaseMetadata("w") { Writes = ["res"] };
        var reader = new ScenePhaseMetadata("r") { Reads = ["res"] };
        Assert.Empty(ResourceAccessValidator.Validate([writer, reader]));
    }

    [Fact]
    public void Validator_ValidateOrThrow_ThrowsOnDanglingRead()
    {
        var reader = new ScenePhaseMetadata("r") { Reads = ["ghost"] };
        Assert.Throws<InvalidOperationException>(() => ResourceAccessValidator.ValidateOrThrow([reader], "s"));
    }
}
