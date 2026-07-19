using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;

namespace Flux.Orchestration.Tests.Execution.Planner;

/// <summary>
/// Tests for the pooled-manifest behaviour of <see cref="SceneRuntimeState"/> that backs zero per-tick
/// allocation in the planner.
/// </summary>
public class SceneRuntimeStateTests
{
    private static SceneRuntimeState NewState(out SceneMetadata scene, out ScenePhaseMetadata phase)
    {
        scene = new SceneMetadata("s", null, [new ScenePhaseMetadata("p")]);
        phase = scene.Phases[0];
        return new SceneRuntimeState(scene, ScenePlanningMode.SnapshotDriven);
    }

    private static ScenePhaseTargetMetadata Target(string method = "M") =>
        new(new ScenePhaseTarget(new object(), method),
            new MethodBindingInfo(MethodCallSignature.Sync, SyncDelegate: (_, _, _) => { }));

    [Fact]
    public void ManifestPool_ReusesSameInstancesAcrossRounds()
    {
        var state = NewState(out var scene, out var phase);
        var ctx = new SceneContext();
        var target = Target();

        // Round 1
        state.ResetManifestRentals();
        var a0 = state.RentManifest(scene, phase, target, ctx);
        var a1 = state.RentManifest(scene, phase, target, ctx);

        // Round 2 — after reset, the pool hands back the very same instances (zero new allocations).
        state.ResetManifestRentals();
        var b0 = state.RentManifest(scene, phase, target, ctx);
        var b1 = state.RentManifest(scene, phase, target, ctx);

        Assert.Same(a0, b0);
        Assert.Same(a1, b1);
        Assert.NotSame(a0, a1);
    }

    [Fact]
    public void ManifestPool_GrowsWhenMoreManifestsNeeded()
    {
        var state = NewState(out var scene, out var phase);
        var ctx = new SceneContext();
        var target = Target();

        state.ResetManifestRentals();
        var first = state.RentManifest(scene, phase, target, ctx);

        // Second round needs two — the pool grows; the first slot is still reused.
        state.ResetManifestRentals();
        var reuse = state.RentManifest(scene, phase, target, ctx);
        var grown = state.RentManifest(scene, phase, target, ctx);

        Assert.Same(first, reuse);
        Assert.NotSame(reuse, grown);
    }

    [Fact]
    public void RentManifest_ReinitializesReusedInstance()
    {
        var state = NewState(out var scene, out var phase);
        var ctx = new SceneContext();

        state.ResetManifestRentals();
        var m = state.RentManifest(scene, phase, Target("First"), ctx);
        var firstTargetInstance = m.Target;

        // Reuse the same physical manifest but with a different target — Set must re-initialize it.
        state.ResetManifestRentals();
        var differentTarget = Target("Second");
        var m2 = state.RentManifest(scene, phase, differentTarget, ctx);

        Assert.Same(m, m2);                                  // same pooled instance
        Assert.Same(differentTarget.Instance, m2.Target);   // but re-initialized to the new target
        Assert.NotSame(firstTargetInstance, m2.Target);
    }

    [Fact]
    public void Plan_IsCompiledForSinglePhaseScene()
    {
        var state = NewState(out _, out _);
        Assert.Single(state.Plan.Levels);
        Assert.Equal("p", state.Plan.Levels[0][0].PhaseId);
    }

    [Fact]
    public void ManifestPlanning_IsAllocationFreeInSteadyState()
    {
        var state = NewState(out var scene, out var phase);
        var ctx = new SceneContext();
        var targets = new[] { Target(), Target(), Target() };

        // One "tick fill" of a level: reset the pool cursor, clear the reusable buffer, rent N manifests.
        void FillOnce()
        {
            state.ResetManifestRentals();
            state.ManifestBuffer.Clear();
            foreach (var t in targets)
                state.ManifestBuffer.Add(state.RentManifest(scene, phase, t, ctx));
        }

        // Warm up: grow the pool and the buffer to their high-water marks (and let the JIT settle).
        for (int i = 0; i < 100; i++) FillOnce();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) FillOnce();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Steady-state planning must allocate nothing on the manifest path.
        Assert.Equal(0, allocated);
    }
}
