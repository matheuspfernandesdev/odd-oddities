using Microsoft.Extensions.DependencyInjection;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Ports;
using OddOddities.Application.Steps;
using OddOddities.Application.UseCases;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering Application layer services.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICategorySelectionPort, SelectBalancedCategoryUseCase>();

        services.AddScoped<IPipelineStep, TextGenerationStep>();
        services.AddScoped<IPipelineStep, ImageGenerationStep>();
        services.AddScoped<IPipelineStep, PublicationStep>();

        services.AddScoped<PipelineOrchestrator>();

        return services;
    }
}
