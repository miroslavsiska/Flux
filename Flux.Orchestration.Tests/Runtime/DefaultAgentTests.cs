using Flux.Orchestration.Exceptions;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Flux.Orchestration.Runtime;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;

namespace Flux.Orchestration.Tests.Runtime;

public class DefaultAgentTests
{
    private readonly Mock<IRegistry> _registryMock = new();
    private readonly Mock<IPlanner> _plannerMock = new();
    private readonly Mock<ILogger<DefaultAgent>> _loggerMock = new();
    private readonly DefaultAgent _sut; // System Under Test

    public DefaultAgentTests()
    {
        _sut = new DefaultAgent(_registryMock.Object, _plannerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteSignalAsync_ShouldDelegateToPlanner()
    {
        // Arrange
        var context = new SceneContext();
        var signal = "OnStart";

        // Act
        await _sut.ExecuteSignalAsync(signal, context);

        // Assert
        _plannerMock.Verify(p => p.PlanSignalAsync(signal, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RegisterPhaseTarget_WhenInstanceIsNull_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _sut.RegisterPhaseTarget("scene", "phase", null!, "method"));
    }

    [Fact]
    public void RegisterPhaseTarget_WhenKeyNotFound_ShouldThrowOrchestrationKeyNotFoundException()
    {
        // Arrange
        var key = new OrchestrationKey("scene", "phase");
        _registryMock.Setup(r => r.Get(key)).Returns((ScenePhase)null!);

        // Act & Assert
        Assert.Throws<OrchestrationKeyNotFoundException>(() => _sut.RegisterPhaseTarget(key, new object(), "Method"));
    }

    [Fact]
    public void DisposalFlow_WhenAgentIsDisposed_ShouldUnregisterAllComponents()
    {
        // Arrange
        var component = new DummyComponent();
        var key = new OrchestrationKey("scene", "phase");
     
        var scenePhase = new ScenePhase(
            phaseId: key.PhaseId,
            targets: [] // Začínáme s prázdným seznamem
        );

        // Setup Mock: When the Agent asks if the phase exists, the Registry returns it
        _registryMock.Setup(r => r.Get(key)).Returns(scenePhase);

        // Act
        // 1. Register the component (DefaultAgent internally calls registry.AddPhaseTarget and creates a DisposalToken)
        _sut.RegisterPhaseTarget(key, component, "Update");

        // 2. Simulate Dispose (either via reflection, or better - if IDisposable is already implemented in DefaultAgent)
        if (_sut is IDisposable disposableAgent)
        {
            disposableAgent.Dispose();
        }
        else
        {
            var field = typeof(DefaultAgent).GetField("_disposalTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);
            var cts = (ObjectDisposalTokenSource)field!.GetValue(_sut)!;
            cts.Dispose();
        }

        // Assert
        // Verify that HandleDisposal in the Agent actually called Unregister on the registry
        _registryMock.Verify(r => r.UnregisterComponent(component), Times.AtLeastOnce);
    }
}

// Helper class for tests
public class DummyComponent : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
    public void Update() { }
}