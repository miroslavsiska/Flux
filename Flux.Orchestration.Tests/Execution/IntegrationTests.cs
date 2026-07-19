using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Flux.Orchestration.Tests.Execution;

//public class IntegrationTests
//{
//    [Fact]
//    public async Task FullFlow_Orchestration_IntegrationTest()
//    {
//        // --- SETUP ---
//        var container = new ServiceCollection()
//            .AddLogging()
//            .AddSingleton<IEngine, DefaultEngine>()
//            .AddSingleton<IScheduler, DefaultScheduler>()
//            .AddSingleton<IRegistry, DefaultRegistry>()
//            .AddSingleton<IPlanner, DefaultPlanner>()
//            .BuildServiceProvider();

//        var planner = container.GetRequiredService<IPlanner>();
//        var scheduler = container.GetRequiredService<IScheduler>();
//        var agent = new BattleAgent(); // Tvůj testovací agent

//        // --- ACT ---
//        // 1. Planner analyzuje agenta a vytvoří plán (seznam manifestů)
//        var plan = planner.CreatePlan(agent);

//        // 2. Scheduler plán vykoná
//        var sw = Stopwatch.StartNew();
//        await scheduler.ScheduleAsync(plan);
//        sw.Stop();

//        // --- ASSERT ---
//        Assert.True(agent.WasUpdated);
//        Assert.True(sw.ElapsedMilliseconds < 10); // Celé flow by i pro jednoho agenta mělo být bleskové
//    }
//}
