using Microsoft.Extensions.DependencyInjection;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Ports;
using OddOddities.Application.Services;
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

        services.AddScoped<IModelSelectionService, ModelSelectionService>();

        // CommentSuggestionStep must run before TextGenerationStep (RF-20 AC1); MS.DI
        // resolves IEnumerable<IPipelineStep> in registration order.
        services.AddScoped<IPipelineStep, CommentSuggestionStep>();
        services.AddScoped<IPipelineStep, TextGenerationStep>();
        services.AddScoped<IPipelineStep, ImageGenerationStep>();
        // VideoGenerationStep must run after ImageGenerationStep (RF-17 AC6); MS.DI
        // resolves IEnumerable<IPipelineStep> in registration order.
        services.AddScoped<IPipelineStep, VideoGenerationStep>();
        services.AddScoped<IPipelineStep, PublicationStep>();

        services.AddScoped<PipelineOrchestrator>();

        return services;
    }
}
