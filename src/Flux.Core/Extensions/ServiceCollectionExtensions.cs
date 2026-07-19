using Flux.Core.Abstractions;
using Flux.Core.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flux.Core.Extensions;

/// <summary>
/// Extension methods for registering Flux services with the .NET dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Flux services (<see cref="IWorkflowEngine"/>) as singletons.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> so calls can be chained.</returns>
    public static IServiceCollection AddFlux(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IWorkflowEngine, WorkflowEngine>();
        return services;
    }
}
