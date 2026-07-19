using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Model;

namespace Flux.Orchestration.Tests.Execution.Planner;

/// <summary>
/// Unit tests for the DAG compiler <see cref="SceneExecutionPlan"/>: explicit dependencies, resource-derived
/// edges, deterministic levelling, cycle/validation failures, and the legacy Parallel/Priority fallback.
/// </summary>
public class SceneExecutionPlanTests
{
    private static ScenePhaseMetadata Phase(
        string id,
        int priority = 0,
        bool parallel = false,
        string[]? dependsOn = null,
        string[]? reads = null,
        string[]? writes = null) =>
        new(phaseId: id, priority: priority, parallel: parallel)
        {
            DependsOn = dependsOn,
            Reads = reads,
            Writes = writes,
        };

    private static List<string> LevelIds(SceneExecutionPlan plan, int level) =>
        plan.Levels[level].Select(p => p.PhaseId).OrderBy(x => x).ToList();

    [Fact]
    public void SinglePhase_ProducesOneLevel()
    {
        var plan = SceneExecutionPlan.Compile([Phase("a")], "s");
        Assert.Single(plan.Levels);
        Assert.Equal("a", plan.Levels[0][0].PhaseId);
    }

    [Fact]
    public void ExplicitChain_OrdersIntoSequentialLevels()
    {
        var plan = SceneExecutionPlan.Compile(
        [
            Phase("c", dependsOn: ["b"]),
            Phase("a"),
            Phase("b", dependsOn: ["a"]),
        ], "s");

        Assert.True(plan.IsDag);
        Assert.Equal(3, plan.Levels.Count);
        Assert.Equal("a", plan.Levels[0][0].PhaseId);
        Assert.Equal("b", plan.Levels[1][0].PhaseId);
        Assert.Equal("c", plan.Levels[2][0].PhaseId);
    }

    [Fact]
    public void Diamond_IndependentPhasesShareALevel()
    {
        // input -> simulate -> {cull, physics} -> render
        var plan = SceneExecutionPlan.Compile(
        [
            Phase("input"),
            Phase("simulate", dependsOn: ["input"]),
            Phase("cull", dependsOn: ["simulate"]),
            Phase("physics", dependsOn: ["simulate"]),
            Phase("render", dependsOn: ["cull", "physics"]),
        ], "s");

        Assert.Equal(4, plan.Levels.Count);
        Assert.Equal(["input"], LevelIds(plan, 0));
        Assert.Equal(["simulate"], LevelIds(plan, 1));
        Assert.Equal(["cull", "physics"], LevelIds(plan, 2));
        Assert.Equal(["render"], LevelIds(plan, 3));
    }

    [Fact]
    public void ResourceEdges_WriterBeforeReader()
    {
        var plan = SceneExecutionPlan.Compile(
        [
            Phase("reader", reads: ["transforms"]),
            Phase("writer", writes: ["transforms"]),
        ], "s");

        Assert.True(plan.IsDag);
        Assert.Equal(2, plan.Levels.Count);
        Assert.Equal("writer", plan.Levels[0][0].PhaseId);
        Assert.Equal("reader", plan.Levels[1][0].PhaseId);
    }

    [Fact]
    public void Cycle_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SceneExecutionPlan.Compile(
            [
                Phase("a", dependsOn: ["b"]),
                Phase("b", dependsOn: ["a"]),
            ], "s"));

        Assert.Contains("cyclic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownDependency_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SceneExecutionPlan.Compile([Phase("a", dependsOn: ["ghost"])], "s"));
    }

    [Fact]
    public void LegacyFallback_GroupsContiguousParallelPhases()
    {
        // No DAG metadata at all → legacy Parallel/Priority grouping.
        var plan = SceneExecutionPlan.Compile(
        [
            Phase("p1", priority: 0, parallel: true),
            Phase("p2", priority: 1, parallel: true),
            Phase("p3", priority: 2, parallel: true),
            Phase("s4", priority: 3, parallel: false),
            Phase("s5", priority: 4, parallel: false),
        ], "s");

        Assert.False(plan.IsDag);
        Assert.Equal(3, plan.Levels.Count);
        Assert.Equal(["p1", "p2", "p3"], LevelIds(plan, 0));
        Assert.Equal(["s4"], LevelIds(plan, 1));
        Assert.Equal(["s5"], LevelIds(plan, 2));
    }
}
