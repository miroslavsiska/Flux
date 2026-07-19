using Flux.Orchestration.Model;

namespace Flux.Orchestration.Resources;

/// <summary>
/// Preflight validation of a scene's declared resource access. Catches mis-declarations that would otherwise
/// surface as a runtime <see cref="KeyNotFoundException"/> or a silent race: a phase that reads a resource no
/// phase writes (dangling read). Cheap, opt-in, intended to run once when a scene is registered.
/// </summary>
public static class ResourceAccessValidator
{
    /// <summary>A single declared-access problem found during validation.</summary>
    /// <param name="PhaseId">The phase that declared the problematic access.</param>
    /// <param name="Resource">The resource name involved.</param>
    /// <param name="Message">Human-readable description.</param>
    public readonly record struct Issue(string PhaseId, string Resource, string Message);

    /// <summary>
    /// Returns the dangling reads in <paramref name="phases"/>: resources a phase declares in <c>Reads</c> that
    /// no phase declares in <c>Writes</c>. An empty result means every declared read has a declared writer.
    /// </summary>
    public static IReadOnlyList<Issue> Validate(IEnumerable<ScenePhaseMetadata> phases)
    {
        var phaseList = phases as IReadOnlyList<ScenePhaseMetadata> ?? phases.ToArray();

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in phaseList)
            if (p.Writes is { Count: > 0 })
                foreach (var w in p.Writes)
                    written.Add(w);

        var issues = new List<Issue>();
        foreach (var p in phaseList)
        {
            if (p.Reads is not { Count: > 0 }) continue;
            foreach (var r in p.Reads)
                if (!written.Contains(r))
                    issues.Add(new Issue(p.PhaseId, r,
                        $"Phase '{p.PhaseId}' reads resource '{r}', which no phase writes (dangling read)."));
        }
        return issues;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> if <see cref="Validate"/> finds any issue.</summary>
    public static void ValidateOrThrow(IEnumerable<ScenePhaseMetadata> phases, string sceneId)
    {
        var issues = Validate(phases);
        if (issues.Count > 0)
            throw new InvalidOperationException(
                $"Scene '{sceneId}' has {issues.Count} resource-access issue(s): " +
                string.Join("; ", issues.Select(i => i.Message)));
    }
}
