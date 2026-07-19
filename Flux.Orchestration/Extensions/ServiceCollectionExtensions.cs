using Flux.Orchestration;
using Flux.Orchestration.Builders;
using Flux.Orchestration.Execution.Engine;
using Flux.Orchestration.Execution.Planer;
using Flux.Orchestration.Execution.Scheduler;
using Flux.Orchestration.MethodBinding;
using Flux.Orchestration.MethodBinding.Builder;
using Flux.Orchestration.Options;
using Flux.Orchestration.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flux.Components.Services.Extensions;

/// <summary>
/// Service collection extensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Flux orchestration services (<see cref="IAgent"/>, registries, engine, scheduler, planner).
    /// </summary>
    /// <remarks>
    /// Options are registered with defaults here; override with <c>services.Configure&lt;OrchestrationOptions&gt;(...)</c>.
    /// The container holds an <see cref="System.IAsyncDisposable"/> singleton (the planner), so prefer disposing it
    /// with <c>await using</c> / <c>DisposeAsync</c>; a synchronous <c>Dispose</c> also works (best-effort teardown).
    /// </remarks>
    /// <param name="services">The service collection to add Flux orchestration to.</param>
    public static void AddFluxOrchestration(this IServiceCollection services)
    {
        services.AddOptions(); // registers IOptions<OrchestrationOptions> with defaults; consumers may Configure<> to override
        services.AddSingleton<IMethodBindingResolver, DefaultMethodBindingResolver>();
        services.AddTransient<ScenePhaseTargetMetadataBuilder>(provider =>
        {
            var resolver = provider.GetRequiredService<IMethodBindingResolver>();
            var options = provider.GetRequiredService<IOptions<OrchestrationOptions>>().Value;
            return new ScenePhaseTargetMetadataBuilder(resolver, options.MethodResolutionMode);
        });

        services.AddScoped<ScenePhaseBuilder>();
        services.AddSingleton<IMetadataFactory, DefaultMetadataFactory>();
        services.AddSingleton<ITargetRegistry, DefaultTargetRegistry>();     
        services.AddSingleton<ISceneMetadataRegistry, DefaultSceneMetadataRegistry>();      
        services.AddSingleton<IRegistry, DefaultRegistry>();

        // execution
        services.AddSingleton<IEngine, DefaultEngine>();
        services.AddSingleton<IScheduler, DefaultScheduler>();

        // DefaultPlanner implements both IPlanner (planning API) and IOrchestrationLifetime (lifecycle).
        // Register as DefaultPlanner first so the singleton instance is shared across both interfaces.
        services.AddSingleton<DefaultPlanner>();
        services.AddSingleton<IPlanner>(sp => sp.GetRequiredService<DefaultPlanner>());
        services.AddSingleton<IOrchestrationLifetime>(sp => sp.GetRequiredService<DefaultPlanner>());

        // orchestrator
        services.AddTransient<IAgent, DefaultAgent>();
    }
}
