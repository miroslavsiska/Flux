using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.MethodBinding.Builder;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Moq;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Orchestration.Tests.Registry;

public class DefaultRegistryTests
{
    private readonly Mock<IScenePhaseTargetMetadataBuilder> _builderMock = new Mock<IScenePhaseTargetMetadataBuilder>();
    private readonly IScenePhaseTargetMetadataBuilder _builder;

    private readonly Mock<IMetadataFactory> _metadataFactoryMock = new Mock<IMetadataFactory>();
    private readonly IMetadataFactory _metadataFactory;

    private readonly Mock<ISceneMetadataRegistry> _metadataRegistryMock = new Mock<ISceneMetadataRegistry>();
    private readonly ISceneMetadataRegistry _metadataRegistry;

    private readonly Mock<ITargetRegistry> _targetRegistryMock = new Mock<ITargetRegistry>();
    private readonly ITargetRegistry _targetRegistry;
  
    private readonly Mock<ILogger<DefaultRegistry>> _loggerMock = new Mock<ILogger<DefaultRegistry>>();
    private readonly ILogger<DefaultRegistry> _logger;


    private readonly IRegistry _registry;

    public DefaultRegistryTests()
    {
        _builder = _builderMock.Object;
        _metadataFactory = _metadataFactoryMock.Object;
        _metadataRegistry = _metadataRegistryMock.Object;
        _targetRegistry = _targetRegistryMock.Object;
        _logger = _loggerMock.Object;
        _registry = new DefaultRegistry(_metadataFactory, _metadataRegistry, _targetRegistry, _logger);
    }

    [Fact]
    public void Register_AddScene_WhenNotAlreadyPresent()
    {
        // Arrange
        var scene = DataHelper.GetScene();

        // Test
        _registry.Add(scene);

        // Assert
        Assert.True(_metadataRegistryMock.Invocations.Count == 1);
        Assert.True(_targetRegistryMock.Invocations.Count == 1);
    }

    [Fact]
    public void Register_GetSceneBySceneId_ShouldReturnScene()
    {
        // Arrange
        var scene = DataHelper.GetScene();
        var sceneMetadata = DataHelper.GetSceneMetadata();
        var target = DataHelper.GetSceneTarget().Target;

        _metadataRegistryMock.Setup(b => b.Resolve("scene1")).Returns(sceneMetadata);
        _targetRegistryMock.Setup(b => b.ResolveAll("scene1")).Returns(
            new Dictionary<string, IReadOnlyList<ScenePhaseTarget>> {
                { "phaseA", new List<ScenePhaseTarget> { target } }
            });
        _registry.Add(scene);

        // Test
        Scene? result = _registry.Get("scene1");

        // Assert
        Assert.NotNull(result);        
        Assert.Equivalent(result, scene);
    }

    [Fact]
    public void Register_GetScenePhase_ShouldReturnScenePhase()
    {
        // Arrange
        var scene = DataHelper.GetScene();
        var scenePhaseMetadata = DataHelper.GetScenePhaseMetadata();
        var target = DataHelper.GetSceneTarget().Target;

        var key = new OrchestrationKey("scene1", "phaseA");
        _metadataRegistryMock.Setup(b => b.Resolve(key)).Returns(scenePhaseMetadata);
        _targetRegistryMock.Setup(b => b.Resolve(key)).Returns([target]);
    
        _registry.Add(DataHelper.GetScene());

        // Test
        ScenePhase? result = _registry.Get(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equivalent(scenePhaseMetadata, result);
    }

    [Fact]
    public void Register_GetAllScenes_ShouldReturnScenes()
    {
        // Arrange
        var scene1 = DataHelper.GetScene("scene1");
        var scene2 = DataHelper.GetScene("scene2");
        var sceneMetadata1 = DataHelper.GetSceneMetadata("scene1");
        var sceneMetadata2 = DataHelper.GetSceneMetadata("scene2");
        var target = DataHelper.GetSceneTarget().Target;

        _metadataRegistryMock.Setup(b => b.ResolveAll()).Returns([sceneMetadata1, sceneMetadata2]);
        _targetRegistryMock.Setup(b => b.ResolveAll("scene1")).Returns(
            new Dictionary<string, IReadOnlyList<ScenePhaseTarget>> {
                { "phaseA", new List<ScenePhaseTarget> { target } }
            }
        );
        _targetRegistryMock.Setup(b => b.ResolveAll("scene2")).Returns(
            new Dictionary<string, IReadOnlyList<ScenePhaseTarget>> {
                { "phaseA", new List<ScenePhaseTarget> { target } }
            });
        _registry.Add(scene1);
        _registry.Add(scene2);

        // Test
        IReadOnlyList<Scene>? result = _registry.GetAll();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count == 2);
    }

    [Fact]
    public void Register_IsRegistered_ShouldReturnTrue()
    {
        // Arrange
        var scene = DataHelper.GetScene("scene1");

        _metadataRegistryMock.Setup(b => b.IsRegistered("scene1")).Returns(true);
        _registry.Add(scene);

        // Test
        bool result = _registry.IsRegistered("scene1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Register_IsRegistered_ShouldReturnFalse()
    {
        // Arrange
        _metadataRegistryMock.Setup(b => b.IsRegistered("scene1")).Returns(false);
      
        // Test
        bool result = _registry.IsRegistered("scene1");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Register_IsValid_ShouldReturnTrue()
    {
        // Arrange
        var scene = DataHelper.GetScene();
        var sceneMetadata = DataHelper.GetSceneMetadata();
        var target = DataHelper.GetSceneTarget().Target;

        _metadataRegistryMock.Setup(b => b.Resolve("scene1")).Returns(sceneMetadata);
        _targetRegistryMock.Setup(b => b.ResolveAll("scene1")).Returns(
         new Dictionary<string, IReadOnlyList<ScenePhaseTarget>> {
                { "phaseA", new List<ScenePhaseTarget> { target } }
         });

        // Test
        var result = _registry.IsValid("scene1", out List<string> missingTargets);

        // Assert
        Assert.True(result);
        Assert.True(missingTargets.Count == 0);
    }

    [Fact]
    public void Register_IsValid_ShouldReturnFalse()
    {
        // Arrange
        var scene = DataHelper.GetScene();
        var sceneMetadata = DataHelper.GetSceneMetadata();

        _metadataRegistryMock.Setup(b => b.Resolve("scene1")).Returns(sceneMetadata);
        _targetRegistryMock.Setup(b => b.ResolveAll("scene1")).Returns(
         new Dictionary<string, IReadOnlyList<ScenePhaseTarget>> {
                { "phaseA", new List<ScenePhaseTarget> {  } } // NO TARGET REGISTERED
         });

        // Test
        var result = _registry.IsValid("scene1", out List<string> missingTargets);

        // Assert
        Assert.False(result);
        Assert.True(missingTargets.Count == 1);
    }

    [Fact]
    public void Register_AddPhaseTarget_ShouldAddPhaseTargetAndMethod()
    {
        // Arrange
        var key = new OrchestrationKey("scene1", "phaseA");
        var scenePhaseMetadata = DataHelper.GetScenePhaseMetadata();
        var target = DataHelper.GetSceneTarget();

        _metadataRegistryMock.Setup(b => b.Resolve(key)).Returns(scenePhaseMetadata);
        _targetRegistryMock.Setup(b => b.Resolve(key)).Returns([target.Target]);

        // Test
        _registry.AddPhaseTarget(key, target.Target, "ToString");

        // Assert
        var targets = _registry.GetTargets(key);
        Assert.Single(targets);
        Assert.Equal(target.Method, targets.First().Type.GetMethod("ToString"));
    }

    [Fact]
    public void Register_RemovePhaseTarget_ShouldRemoveInstance()
    {
        // Arrange
        var key = new OrchestrationKey("scene1", "phaseA");
        var scenePhaseMetadata = DataHelper.GetScenePhaseMetadata();
        var target = DataHelper.GetSceneTarget();

        _metadataRegistryMock.Setup(b => b.Resolve(key)).Returns(scenePhaseMetadata);
        // _targetRegistryMock.Setup(b => b.Resolve(key)).Returns([target.Target]);
        _registry.AddPhaseTarget(key, target.Target, "ToString");

        // Test
        _registry.RemovePhaseTarget(key, target.Target);

        // Assert
        var targets = _registry.GetTargets(key);
        Assert.Null(targets);
    }

    [Fact]
    public void Register_AddPhaseTarget_ShouldAddPhaseTarget()
    {
        // Arrange
        var key = new OrchestrationKey("scene1", "phaseA");
        var scenePhaseMetadata = DataHelper.GetScenePhaseMetadata();
        var target = DataHelper.GetSceneTarget().Target;

        _metadataRegistryMock.Setup(b => b.Resolve(key)).Returns(scenePhaseMetadata);
        _targetRegistryMock.Setup(b => b.Resolve(key)).Returns([target]);

        // Test
        _registry.AddPhaseTarget(key, target);

        // Assert
        var targets = _registry.GetTargets(key);
        Assert.Single(targets);
    }

    [Fact]
    public void Register_RemovePhaseTarget_ShouldRemovePhaseTarget()
    {
        // Arrange
        var key = new OrchestrationKey("scene1", "phaseA");
        var scenePhaseMetadata = DataHelper.GetScenePhaseMetadata();
        var target = DataHelper.GetSceneTarget().Target;

        _metadataRegistryMock.Setup(b => b.Resolve(key)).Returns(scenePhaseMetadata);
        _targetRegistryMock.Setup(b => b.Resolve(key)).Returns([target]);
        _registry.AddPhaseTarget(key, target);

        // Test
        _registry.RemovePhaseTarget(key, target);   

        // Assert
        var targets = _registry.GetTargets(key);
        Assert.Single(targets);
    }

    [Fact]
    public void Register_GetTargets_ShouldReturnTargets()
    {
        // Arrange
        var key = new OrchestrationKey("scene1", "phaseA");
        var scenePhaseMetadata = DataHelper.GetScenePhaseMetadata();
        var target = DataHelper.GetSceneTarget();

        _metadataRegistryMock.Setup(b => b.Resolve(key)).Returns(scenePhaseMetadata);
        _targetRegistryMock.Setup(b => b.Resolve(key)).Returns([target.Target]);
        _registry.AddPhaseTarget(key, target.Target);

        // Test
        var targets = _registry.GetTargets(key);

        // Assert
        Assert.Single(targets);
    }
}
