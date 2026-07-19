using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.MethodBinding.Builder;
using Flux.Orchestration.Model;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Flux.Orchestration.Tests.Runtime;

public class DefaultAgent_PerformanceTest
{
    //[Fact]
    //public async Task MassDisposal_PerformanceTest()
    //{
    //    // 1. SETUP
    //    var registry = new DefaultRegistry(); // Použijeme reálnou (ne mockovanou) verzi, pokud je hotová
    //    var planer = new Mock<IPlanner>().Object;
    //    var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultAgent>.Instance;

    //    var agent = new DefaultAgent(registry, planer, logger);
    //    int count = 10000;

    //    // Připravíme scénu a fázi v registru
    //    var key = new OrchestrationKey("PerfScene", "UpdatePhase");
    //    registry.Add(new Scene("PerfScene", [new ScenePhase("UpdatePhase", [])]));

    //    // Registrace 10 000 komponent
    //    for (int i = 0; i < count; i++)
    //    {
    //        var component = new DummyComponent();
    //        agent.RegisterPhaseTarget(key, component, nameof(DummyComponent.Update));
    //    }

    //    // 2. MEASURE - Synchronous Disposal
    //    Console.WriteLine($"Starting disposal of {count} components...");
    //    var sw = Stopwatch.StartNew();

    //    agent.Dispose(); // Tady se spustí celá ta hierarchická lavina

    //    sw.Stop();

    //    // 3. RESULTS
    //    double totalMs = sw.Elapsed.TotalMilliseconds;
    //    Console.WriteLine("---------- Mass Disposal Performance ----------");
    //    Console.WriteLine($"Total time for {count} components: {totalMs:F2} ms");
    //    Console.WriteLine($"Average time per component: {(totalMs * 1000) / count:F4} microseconds");
    //    Console.WriteLine("-----------------------------------------------");

    //    // Validace, že v registru nic nezbylo
    //    Assert.False(agent.IsPhaseRegistered(key));
    //}


    [Fact]
    public void HighLoad_RegistrationAndDisposal_Performance()
    {
        // Arrange
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var targetBuilder = new ScenePhaseTargetMetadataBuilder(
            new DefaultMethodBindingResolver(), // Předpokládám tvůj resolver
            MethodResolutionMode.Flexible);

        var targetRegistry = new DefaultTargetRegistry(targetBuilder, loggerFactory.CreateLogger<DefaultTargetRegistry>());
        var metadataRegistry = new DefaultSceneMetadataRegistry(loggerFactory.CreateLogger<DefaultSceneMetadataRegistry>());
        var registry = new DefaultRegistry(new DefaultMetadataFactory(), metadataRegistry, targetRegistry, loggerFactory.CreateLogger<DefaultRegistry>());
        var agent = new DefaultAgent(registry, new Mock<IPlanner>().Object, loggerFactory.CreateLogger<DefaultAgent>());

        int agentCount = 1000;
        int componentsPerAgent = 10;
        var key = new OrchestrationKey("WorldScene", "UpdatePhase");

        // We must have the scene in the metadata, otherwise the Agent will throw an exception
        registry.Add(new Scene("WorldScene", "WorldScene description", [new ScenePhase("UpdatePhase", [])]));

        // MEASURE REGISTRATION
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < agentCount; i++)
        {
            for (int j = 0; j < componentsPerAgent; j++)
            {
                agent.RegisterPhaseTarget(key, new DummyComponent(), "Update");
            }
        }
        sw.Stop();
        var regTime = sw.ElapsedMilliseconds;

        // MEASURE DISPOSAL
        sw.Restart();
        agent.Dispose(); // This triggers the hierarchical cleanup
        sw.Stop();

        Console.WriteLine($"Registration of {agentCount * componentsPerAgent} targets: {regTime}ms");
        Console.WriteLine($"Disposal of everything: {sw.Elapsed.TotalMilliseconds}ms");

        // VALIDATION
        Assert.Equal(0, targetRegistry.ResolveMetadata(key).Count);
    }

}
