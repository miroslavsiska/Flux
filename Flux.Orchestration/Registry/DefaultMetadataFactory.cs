using Flux.Orchestration.Attributes;
using Flux.Orchestration.Model;
using Flux.Orchestration.Model.Base;
using System.Reflection;

namespace Flux.Orchestration.Registry;

/// <summary>Default <see cref="IMetadataFactory"/>: builds <see cref="SceneMetadata"/> from a type's attributes.</summary>
public class DefaultMetadataFactory : IMetadataFactory
{
    /// <inheritdoc />
    public SceneMetadata CreateFrom(Type type)
    {
        // Scene attribute
        var sceneAttr = type.GetCustomAttribute<SceneAttribute>();
        if (sceneAttr == null)
            throw new InvalidOperationException($"Type '{type.Name}' is not marked with [SceneAttribute].");

        // Phases are declared by class-level [ScenePhase] attributes (the reconciled model: the class
        // defines phases; [SceneMethod] on methods binds targets to them — see DefaultRegistry.RegisterComponent).
        var phaseMetadata = new List<ScenePhaseMetadata>();
        foreach (var phaseAttr in type.GetCustomAttributes<ScenePhaseAttribute>(inherit: false))
        {
            var parameters = new Dictionary<string, object>();
            if (phaseAttr.Parameters is { Length: > 0 })
            {
                foreach (var entry in phaseAttr.Parameters)
                {
                    var parts = entry.Split('=', 2);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = (object)parts[1].Trim();

                    parameters[key] = value;
                }
            }

#pragma warning disable CS0618 // Parallel is deprecated but still honoured as a DAG fallback.
            var legacyParallel = phaseAttr.Parallel;
#pragma warning restore CS0618
            var metadata = new ScenePhaseMetadata(
                phaseAttr.PhaseId,
                phaseAttr.Description,
                phaseAttr.Category,
                phaseAttr.Tags,
                phaseAttr.Priority,
                legacyParallel,
                phaseAttr.Timeout,
                phaseAttr.MaxRetries,
                phaseAttr.Logging,
                parameters)
            {
                DependsOn = phaseAttr.DependsOn,
                Reads = phaseAttr.Reads,
                Writes = phaseAttr.Writes,
                SequentialTargets = phaseAttr.SequentialTargets,
            };

            phaseMetadata.Add(metadata);
        }

        // SceneMetadata
        var triggers = new List<SignalBinding>();
        if (sceneAttr.Triggers is { Length: > 0 })
        {
            foreach (var entry in sceneAttr.Triggers)
            {
                var parts = entry.Split('=', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = (object)parts[1].Trim();

               var trigger = new SignalBinding()
               { 
                   Signal = key, 
                   Description = value.ToString() 
               };
                triggers.Add(trigger);
            }
        }

#pragma warning disable CS0618 // Parallel is deprecated but still honoured as a DAG fallback.
        var legacySceneParallel = sceneAttr.Parallel;
#pragma warning restore CS0618
        return new SceneMetadata(
            sceneAttr.Id ?? type.Name,
            sceneAttr.Description,
            phaseMetadata,
            triggers,
            sceneAttr.Category,
            sceneAttr.Tags,
            sceneAttr.ScenePlanningMode,
            sceneAttr.PlanningInterval,
            sceneAttr.Priority,
            legacySceneParallel,
            sceneAttr.Logging);
    }
}
