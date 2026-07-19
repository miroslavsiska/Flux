using Flux.Orchestration.Attributes;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.MethodBinding.Builder;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flux.Orchestration.Tests.Registry;

/// <summary>
/// Tests for the (now-reconciled) attribute model: [ScenePhase] on the class defines phases; [SceneMethod]
/// on methods binds targets to phases.
/// </summary>
public class AttributeRegistrationTests
{
    private const string SceneId = "RegScene";

    [Scene(Id = SceneId)]
    [ScenePhase(SceneId = SceneId, PhaseId = "A", Priority = 1)]
    [ScenePhase(SceneId = SceneId, PhaseId = "B", DependsOn = new[] { "A" })]
    private sealed class SampleScene
    {
        public int Calls;

        [SceneMethod(SceneId = SceneId, PhaseId = "A")]
        public void OnA(SceneContext context, CancellationToken token) => Calls++;
    }

    [Fact]
    public void Factory_ReadsPhasesFromClassLevelAttributes()
    {
        var scene = new DefaultMetadataFactory().CreateFrom(typeof(SampleScene));

        Assert.Equal(SceneId, scene.Id);
        Assert.Equal(2, scene.Phases.Count);
        Assert.Contains(scene.Phases, p => p.PhaseId == "A");

        var b = scene.Phases.First(p => p.PhaseId == "B");
        Assert.NotNull(b.DependsOn);
        Assert.Contains("A", b.DependsOn!);
    }

    [Fact]
    public void RegisterComponent_BindsSceneMethodAnnotatedMethods()
    {
        var resolver = new DefaultMethodBindingResolver();
        var builder = new ScenePhaseTargetMetadataBuilder(resolver, MethodResolutionMode.Flexible);
        var targetRegistry = new DefaultTargetRegistry(builder, NullLogger<DefaultTargetRegistry>.Instance);
        var sceneRegistry = new DefaultSceneMetadataRegistry(NullLogger<DefaultSceneMetadataRegistry>.Instance);
        var registry = new DefaultRegistry(
            new DefaultMetadataFactory(), sceneRegistry, targetRegistry, NullLogger<DefaultRegistry>.Instance);

        // 1) Phase definitions from the class.
        registry.Register(typeof(SampleScene));
        // 2) Method-to-phase binding from [SceneMethod].
        var handler = new SampleScene();
        registry.RegisterComponent(handler);

        var targets = registry.GetTargets(new OrchestrationKey(SceneId, "A")).ToList();

        var target = Assert.Single(targets);
        Assert.Same(handler, target.Instance);
        Assert.Equal(nameof(SampleScene.OnA), target.MethodName);
    }
}
