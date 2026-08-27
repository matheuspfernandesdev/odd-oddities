using Microsoft.Extensions.DependencyInjection;
using OddOddities.Application.Services;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Adapters;

namespace OddOddities.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Infrastructure layer services (adapters, repositories).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Repositories (scoped: one instance per request/scope)
        services.AddScoped<IPostRepository, PostgresPostRepository>();

        // Application services (scoped)
        services.AddScoped<ICategorySelectionPort, CategorySelectionService>();

        return services;
    }
}