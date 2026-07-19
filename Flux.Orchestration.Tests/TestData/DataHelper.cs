using Flux.Orchestration.Model;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Orchestration.Tests;

internal class DataHelper
{
    internal static (ScenePhaseTarget Target, MethodInfo? Method) GetSceneTarget()
    {
        var target = new ScenePhaseTarget(new object(), "ToString");
        var method = target.Type.GetMethod("ToString");
        return (target, method);
    }

    internal static ScenePhase GetScenePhase(string phaseId = "phaseA")
    {
            return new ScenePhase(
                phaseId: phaseId,
                targets: [GetSceneTarget().Target],
                description: "Validates the shopping cart",
                category: "Scene category",
                tags: ["test"],
                priority: 0,
                parallel: false,
                timeout: TimeSpan.FromSeconds(53),
                maxRetries: 3,
                logging: true,
                parameters: []
            );
    }

    internal static ScenePhaseMetadata GetScenePhaseMetadata(string phaseId = "phaseA")
    {
        return new ScenePhaseMetadata(
             phaseId: phaseId,
             description: "Validates the shopping cart",
             category: "Scene category",
             tags: ["test"],
             priority: 0,
             parallel: false,
             timeout: TimeSpan.FromSeconds(53),
             maxRetries: 3,
             logging: true,
             parameters: []);
    }

    internal static Scene GetScene(string sceneId = "scene1", params string[] phases)
    {
        var phasesList = phases.Length == 0 ? [GetScenePhase()] : phases.Select(p => GetScenePhase(p)).ToList();

        var target = GetSceneTarget().Target;
        var method = GetSceneTarget().Method;

        var scene = new Scene(
            sceneId: sceneId,
            description: "Sample scene for testing",
            phases: phasesList,
            triggers: [
                new SignalBinding() { Signal = "scene1TestSignal", Description = "Test signal description" }
            ],
            category: "Scene category",
            tags: ["test"],
            priority: 0,
            parallel: false,
            logging: false
        );
        return scene;
    }

    internal static SceneMetadata GetSceneMetadata(string sceneId = "scene1", params string[] phases)
    {
        var phasesList = phases.Length == 0 ?  [GetScenePhaseMetadata()] : phases.Select(p => GetScenePhaseMetadata(p)).ToList();

        var sceneMetadata = new SceneMetadata(
            sceneId: sceneId,
            description: "Sample scene for testing",
            phases: phasesList,
            triggers: [
                new SignalBinding() { Signal = $"{sceneId}TestSignal", Description = "Test signal description" }
            ],
            category: "Scene category",
            tags: ["test"],
            priority: 0,
            parallel: false,
            logging: false
        );
        return sceneMetadata;
    }
}
