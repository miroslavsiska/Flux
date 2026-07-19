namespace Flux.Orchestration.Model;

/// <summary>
/// Controls how and when the planner triggers scene planning (e.g. immediate, fixed interval, or manual).
/// </summary>
public enum ScenePlanningMode
{
    SnapshotDriven,   // každá invalidace → plánuj hned
    Aggregate,        // sbírej signály → plánuj jednou za X ms
    Immediate,        // plánuj každý tick
    FixedTimestep,    // plánuj v pevné frekvenci (např. 60 Hz)
    Transition,       // plánuj ve vysoké frekvenci (morph, fade…)
    Manual            // neplánuj nic → plánování spouští signály
}
