using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OddOddities.Application.Services;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;
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

        // Application services
        services.AddScoped<ICategorySelectionPort, CategorySelectionService>();

        // Schedule service (singleton: shared across all scopes, thread-safe)
        services.AddSingleton<ISchedulerPort, ScheduleService>();

        // Logging correlation (singleton: shared across all scopes)
        services.AddSingleton<ILogCorrelationPort, LogCorrelationService>();

        // Source validation (RF-08): HttpClient with 10s timeout and max 3 redirects
        services.AddHttpClient(nameof(SourceValidationService), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0 (SourceValidator)");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            // Prevent credential leakage on redirects
            UseDefaultCredentials = false
        });

        services.AddScoped<ISourceValidationPort, SourceValidationService>();

        // Similarity checking (RF-07): ContentHash + Jaccard similarity for duplicate detection
        services.AddScoped<ISimilarityCheckPort, SimilarityCheckService>();

        // Image processing (RF-09): ImageSharp resize, watermark, JPEG encoding
        services.AddScoped<IImageProcessingPort, ImageSharpProcessingService>();

        // Object storage (RF-04): MinIO with quota verification (BR-009)
        // Singleton: AmazonS3Client is thread-safe and should be reused
        services.AddSingleton<IObjectStoragePort, MinioObjectStorageAdapter>();

        return services;
    }
}