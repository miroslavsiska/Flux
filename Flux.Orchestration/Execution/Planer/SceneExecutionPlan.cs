using Flux.Orchestration.Model;

namespace Flux.Orchestration.Execution.Planer;

/// <summary>
/// A precompiled, immutable execution plan for a scene's phases, expressed as ordered execution levels.
/// </summary>
/// <remarks>
/// The plan is produced once during preflight (see <see cref="Compile"/>) and cached on the scene's runtime
/// state, so the per-tick cost is zero — the planner simply walks <see cref="Levels"/>.
/// <para>
/// Phases on the same level have no ordering relationship and may run concurrently; the planner inserts a
/// barrier between levels. Edges are derived from explicit <see cref="Model.Base.ScenePhaseBase.DependsOn"/>
/// declarations and from resource access (<see cref="Model.Base.ScenePhaseBase.Reads"/> /
/// <see cref="Model.Base.ScenePhaseBase.Writes"/>). When a scene declares no DAG metadata at all, the
/// compiler falls back to the legacy <c>Parallel</c> + <c>Priority</c> grouping for backward compatibility.
/// </para>
/// </remarks>
public sealed class SceneExecutionPlan
{
    /// <summary>
    /// Ordered execution levels. Each inner list is a set of phases with no mutual ordering constraint;
    /// levels execute in order with a barrier between them.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ScenePhaseMetadata>> Levels { get; }

    /// <summary>
    /// True when the plan was produced from explicit DAG metadata; false when it fell back to the
    /// legacy <c>Parallel</c>/<c>Priority</c> grouping.
    /// </summary>
    public bool IsDag { get; }

    private SceneExecutionPlan(IReadOnlyList<IReadOnlyList<ScenePhaseMetadata>> levels, bool isDag)
    {
        Levels = levels;
        IsDag = isDag;
    }

    /// <summary>
    /// Compiles an execution plan from a scene's phases. Validates that every dependency reference resolves
    /// and that the resulting graph is acyclic; throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="phases">The scene's phases (any order).</param>
    /// <param name="sceneId">Scene id, used only for diagnostics.</param>
    public static SceneExecutionPlan Compile(IReadOnlyList<ScenePhaseMetadata> phases, string sceneId)
    {
        if (phases is null || phases.Count == 0)
            return new SceneExecutionPlan([], isDag: false);

        bool anyDagMetadata = false;
        foreach (var p in phases)
        {
            if (HasItems(p.DependsOn) || HasItems(p.Reads) || HasItems(p.Writes))
            {
                anyDagMetadata = true;
                break;
            }
        }

        return anyDagMetadata
            ? CompileDag(phases, sceneId)
            : CompileLegacy(phases);
    }

    // ── DAG compilation ───────────────────────────────────────────────────────

    private static SceneExecutionPlan CompileDag(IReadOnlyList<ScenePhaseMetadata> phases, string sceneId)
    {
        // Index phases by id; detect duplicates early — they make ordering ambiguous.
        var byId = new Dictionary<string, ScenePhaseMetadata>(phases.Count, StringComparer.Ordinal);
        foreach (var p in phases)
        {
            if (!byId.TryAdd(p.PhaseId, p))
                throw new InvalidOperationException(
                    $"Scene '{sceneId}' declares duplicate phase id '{p.PhaseId}'; phase ids must be unique for DAG compilation.");
        }

        // Adjacency: edge u -> v means u must run before v. Use a set per node to dedupe edges.
        var edges = new Dictionary<string, HashSet<string>>(phases.Count, StringComparer.Ordinal);
        foreach (var p in phases)
            edges[p.PhaseId] = new HashSet<string>(StringComparer.Ordinal);

        void AddEdge(string from, string to)
        {
            if (!string.Equals(from, to, StringComparison.Ordinal))
                edges[from].Add(to);
        }

        // 1) Explicit dependencies: each listed phase must run before this one.
        foreach (var p in phases)
        {
            if (!HasItems(p.DependsOn)) continue;
            foreach (var dep in p.DependsOn!)
            {
                if (!byId.ContainsKey(dep))
                    throw new InvalidOperationException(
                        $"Scene '{sceneId}', phase '{p.PhaseId}' declares DependsOn '{dep}', which is not a known phase.");
                AddEdge(dep, p.PhaseId);
            }
        }

        // 2) Resource edges. Group writers/readers per resource.
        var writers = new Dictionary<string, List<ScenePhaseMetadata>>(StringComparer.Ordinal);
        var readers = new Dictionary<string, List<ScenePhaseMetadata>>(StringComparer.Ordinal);
        foreach (var p in phases)
        {
            if (HasItems(p.Writes))
                foreach (var res in p.Writes!)
                    (writers.TryGetValue(res, out var w) ? w : writers[res] = []).Add(p);
            if (HasItems(p.Reads))
                foreach (var res in p.Reads!)
                    (readers.TryGetValue(res, out var r) ? r : readers[res] = []).Add(p);
        }

        // Deterministic ordering comparer for tie-breaks: higher Priority first, then PhaseId.
        static int Order(ScenePhaseMetadata a, ScenePhaseMetadata b)
        {
            int c = b.Priority.CompareTo(a.Priority);
            return c != 0 ? c : string.CompareOrdinal(a.PhaseId, b.PhaseId);
        }

        foreach (var (res, ws) in writers)
        {
            // Write-after-write: order writers deterministically and chain them so two writers of the
            // same resource never run concurrently (avoids a data race) — and never form a cycle.
            ws.Sort(Order);
            for (int i = 1; i < ws.Count; i++)
                AddEdge(ws[i - 1].PhaseId, ws[i].PhaseId);

            // Write-before-read: readers see the final write, so they depend on the last writer.
            if (readers.TryGetValue(res, out var rs))
            {
                var lastWriter = ws[^1];
                foreach (var reader in rs)
                    AddEdge(lastWriter.PhaseId, reader.PhaseId);
            }
        }

        // 3) Kahn's algorithm → levels.
        var indegree = new Dictionary<string, int>(phases.Count, StringComparer.Ordinal);
        foreach (var p in phases) indegree[p.PhaseId] = 0;
        foreach (var (_, tos) in edges)
            foreach (var to in tos)
                indegree[to]++;

        var levels = new List<IReadOnlyList<ScenePhaseMetadata>>();
        var ready = new List<ScenePhaseMetadata>();
        foreach (var p in phases)
            if (indegree[p.PhaseId] == 0) ready.Add(p);

        int placed = 0;
        while (ready.Count > 0)
        {
            ready.Sort(Order);
            levels.Add(ready.ToArray());
            placed += ready.Count;

            var next = new List<ScenePhaseMetadata>();
            foreach (var node in ready)
                foreach (var to in edges[node.PhaseId])
                    if (--indegree[to] == 0)
                        next.Add(byId[to]);

            ready = next;
        }

        if (placed != phases.Count)
            throw new InvalidOperationException(
                $"Scene '{sceneId}' has a cyclic phase dependency graph; cannot compile an execution plan.");

        return new SceneExecutionPlan(levels, isDag: true);
    }

    // ── Legacy fallback (Parallel + Priority) ─────────────────────────────────

    private static SceneExecutionPlan CompileLegacy(IReadOnlyList<ScenePhaseMetadata> phases)
    {
        // Reproduces the historical behaviour: phases are ordered by priority (lower first, matching the
        // previous OrderBy(Priority)); a contiguous run of Parallel phases shares one level, while each
        // non-parallel phase forms its own barrier level.
        var sorted = phases.OrderBy(p => p.Priority).ToList();
        var levels = new List<IReadOnlyList<ScenePhaseMetadata>>();

        List<ScenePhaseMetadata>? current = null;
        foreach (var p in sorted)
        {
#pragma warning disable CS0618 // legacy fallback intentionally reads the deprecated flag
            bool parallel = p.Parallel;
#pragma warning restore CS0618
            if (parallel)
            {
                (current ??= []).Add(p);
            }
            else
            {
                if (current is { Count: > 0 })
                {
                    levels.Add(current.ToArray());
                    current = null;
                }
                levels.Add(new[] { p });
            }
        }
        if (current is { Count: > 0 })
            levels.Add(current.ToArray());

        return new SceneExecutionPlan(levels, isDag: false);
    }

    private static bool HasItems(IReadOnlyList<string>? list) => list is { Count: > 0 };
}
