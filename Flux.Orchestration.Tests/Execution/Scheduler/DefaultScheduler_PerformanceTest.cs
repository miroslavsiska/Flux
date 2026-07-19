using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.Model;
using System.Diagnostics;

namespace Flux.Orchestration.Tests.Execution.Scheduler;

public class DefaultScheduler_PerformanceTest
{
    /// <summary>
    /// Measures the performance of scheduling 10,000 synchronous actor updates over 100 frames using the default scheduler.
    /// </summary>
    /// <remarks>
    /// This test disables logging to focus on raw scheduling performance and includes a warm-up
    /// phase to account for JIT compilation. The results are output to the console, including total and average frame
    /// times, as well as average dispatch time per task.
    /// </remarks>
    /// <returns>A task that represents the asynchronous execution of the performance test.</returns>
    [Fact]
    public async Task Scheduler_With_Synchronous_MethodCallSignature_PerformanceTest()
    {
        var engine = new DefaultEngine();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultScheduler>.Instance;
        var scheduler = new DefaultScheduler(engine, logger);

        // 1. SETUP: We create a simple scene with 10,000 actors, each having a single "Update" method to call. 
        var context = new SceneContext();
        var sceneMeta = new SceneMetadata(
            sceneId: "MainSim",
            description: "Core Simulation Loop",
            phases: [], // Phases in the build are not needed directly for the manifest
            parallel: true,
            logging: false // For performance testing, we disable logging to measure raw execution time without overhead
        );

        var phaseMeta = new ScenePhaseMetadata(
            phaseId: "Update",
            parallel: true, // This phase can run in parallel
            logging: false
        );

        // 2. GENERATE: 10,000 manifests (Each has its own target, but shares metadata)
        var manifests = new List<ScenePhaseManifest>(10000);
        for (int i = 0; i < 10000; i++)
        {
            var actor = new DummyActor();
            var target = new ScenePhaseTarget(actor, "OnUpdate");

            // Simulate method binding (MethodBindingInfo)
            var binding = new MethodBindingInfo(
                Signature: MethodCallSignature.Sync,
                SyncDelegate: (target, context, token) =>
                {
                    // Here the actual work is performed
                    ((DummyActor)target).OnUpdate();
                }
            );

            //// Example for ValueTask (the most modern approach)
            //var valueTaskBinding = new MethodBindingInfo(
            //    Signature: MethodCallSignature.ValueTask,
            //    ValueTaskDelegate: (target, context, token) =>
            //    {
            //        return ((DummyActor)target).OnUpdateValueTaskAsync(); 
            //    }
            //);

            var targetMeta = new ScenePhaseTargetMetadata(target, binding);

            manifests.Add(new ScenePhaseManifest(sceneMeta, phaseMeta, targetMeta, context));
        }

        // 3. WARM-UP: Let the JIT compile the code
        await scheduler.ScheduleAsync(manifests);

        // 4. MEASURE: Simulation of the game loop
        Console.WriteLine("Running benchmark: 10,000 actors over 100 frames...");
        var sw = Stopwatch.StartNew();

        for (int f = 0; f < 100; f++)
        {
            await scheduler.ScheduleAsync(manifests);
        }

        sw.Stop();

        // 5. RESULTS
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double avgMs = totalMs / 100.0;

        Console.WriteLine("---------- Synchronous_MethodCallSignature ----------");
        Console.WriteLine($"Total time (100 frames): {totalMs:F2} ms");
        Console.WriteLine($"Average per frame: {avgMs:F4} ms");
        Console.WriteLine($"One task (dispatch) took on average: {(avgMs * 1000) / 10000:F4} microseconds");
        Console.WriteLine("---------- Synchronous_MethodCallSignature ----------");
    }

    /// <summary>
    /// Measures the performance of scheduling 10,000 ValueTask-based actor updates over 100 frames using the default scheduler.
    /// </summary>
    /// <remarks>
    /// This test disables logging to focus on raw scheduling performance and includes a warm-up
    /// phase to account for JIT compilation. The results are output to the console, including total and average frame
    /// times, as well as average dispatch time per task.
    /// </remarks>
    /// <returns>A task that represents the asynchronous execution of the performance test.</returns>
    [Fact]
    public async Task Scheduler_With_ValueTask_MethodCallSignature_PerformanceTest()
    {
        var engine = new DefaultEngine(); 
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultScheduler>.Instance;
        var scheduler = new DefaultScheduler(engine, logger);

        // 1. SETUP: We create a simple scene with 10,000 actors, each having a single "Update" method to call. 
        var context = new SceneContext();
        var sceneMeta = new SceneMetadata(
            sceneId: "MainSim",
            description: "Core Simulation Loop",
            phases: [], // Phases in the build are not needed directly for the manifest
            parallel: true,
            logging: false // For performance testing, we disable logging to measure raw execution time without overhead
        );

        var phaseMeta = new ScenePhaseMetadata(
            phaseId: "Update",
            parallel: true, // This phase can run in parallel
            logging: false
        );

        // 2. GENERATE: 10,000 manifests (Each has its own target, but shares metadata)
        var manifests = new List<ScenePhaseManifest>(10000);
        for (int i = 0; i < 10000; i++)
        {
            var actor = new DummyActor();
            var target = new ScenePhaseTarget(actor, "OnUpdateValueTaskAsync");

            // Simulate method binding (MethodBindingInfo)
            var binding = new MethodBindingInfo(
                Signature: MethodCallSignature.ValueTask,
                ValueTaskDelegate: (target, context, token) =>
                {
                    return ((DummyActor)target).OnUpdateValueTaskAsync();
                }
            );

            var targetMeta = new ScenePhaseTargetMetadata(target, binding);

            manifests.Add(new ScenePhaseManifest(sceneMeta, phaseMeta, targetMeta, context));
        }

        // 3. WARM-UP: Let the JIT compile the code
        await scheduler.ScheduleAsync(manifests);

        // 4. MEASURE: Simulation of the game loop
        Console.WriteLine("Running benchmark: 10,000 actors over 100 frames...");
        var sw = Stopwatch.StartNew();

        for (int f = 0; f < 100; f++)
        {
            await scheduler.ScheduleAsync(manifests);
        }

        sw.Stop();

        // 5. RESULTS
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double avgMs = totalMs / 100.0;

        Console.WriteLine("---------- ValueTask_MethodCallSignature ----------");
        Console.WriteLine($"Total time (100 frames): {totalMs:F2} ms");
        Console.WriteLine($"Average per frame: {avgMs:F4} ms");
        Console.WriteLine($"One task (dispatch) took on average: {(avgMs * 1000) / 10000:F4} microseconds");
        Console.WriteLine("---------- ValueTask_MethodCallSignature ----------");
    }


    /// <summary>
    /// Measures the performance of scheduling 10,000 Task-based actor updates over 100 frames using the default scheduler.
    /// </summary>
    /// <remarks>
    /// This test disables logging to focus on raw scheduling performance and includes a warm-up
    /// phase to account for JIT compilation. The results are output to the console, including total and average frame
    /// times, as well as average dispatch time per task.
    /// </remarks>
    /// <returns>A task that represents the asynchronous execution of the performance test.</returns>
    [Fact]
    public async Task Scheduler_With_Task_MethodCallSignature_PerformanceTest()
    {
        var engine = new DefaultEngine(); 
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultScheduler>.Instance;
        var scheduler = new DefaultScheduler(engine, logger);

        // 1. SETUP: We create a simple scene with 10,000 actors, each having a single "Update" method to call. 
        var context = new SceneContext();
        var sceneMeta = new SceneMetadata(
            sceneId: "MainSim",
            description: "Core Simulation Loop",
            phases: [], // Phases in the build are not needed directly for the manifest
            parallel: true,
            logging: false // For performance testing, we disable logging to measure raw execution time without overhead
        );

        var phaseMeta = new ScenePhaseMetadata(
            phaseId: "Update",
            parallel: true, // This phase can run in parallel
            logging: false
        );

        // 2. GENERATE: 10,000 manifests (Each has its own target, but shares metadata)
        var manifests = new List<ScenePhaseManifest>(10000);
        for (int i = 0; i < 10000; i++)
        {
            var actor = new DummyActor();
            var target = new ScenePhaseTarget(actor, "OnUpdateTaskAsync");

            // Simulate method binding (MethodBindingInfo)
            var binding = new MethodBindingInfo(
                Signature: MethodCallSignature.Task,
                TaskDelegate: (target, context, token) =>
                {
                    return ((DummyActor)target).OnUpdateTaskAsync();
                }
            );

            var targetMeta = new ScenePhaseTargetMetadata(target, binding);

            manifests.Add(new ScenePhaseManifest(sceneMeta, phaseMeta, targetMeta, context));
        }

        // 3. WARM-UP: Let the JIT compile the code
        await scheduler.ScheduleAsync(manifests);

        // 4. MEASURE: Simulation of the game loop
        Console.WriteLine("Running benchmark: 10,000 actors over 100 frames...");
        var sw = Stopwatch.StartNew();

        for (int f = 0; f < 100; f++)
        {
            await scheduler.ScheduleAsync(manifests);
        }

        sw.Stop();

        // 5. RESULTS
        double totalMs = sw.Elapsed.TotalMilliseconds;
        double avgMs = totalMs / 100.0;

        Console.WriteLine("------------ Task_MethodCallSignature ------------");
        Console.WriteLine($"Total time (100 frames): {totalMs:F2} ms");
        Console.WriteLine($"Average per frame: {avgMs:F4} ms");
        Console.WriteLine($"One task (dispatch) took on average: {(avgMs * 1000) / 10000:F4} microseconds");
        Console.WriteLine("------------ Task_MethodCallSignature ------------");
    }
}
