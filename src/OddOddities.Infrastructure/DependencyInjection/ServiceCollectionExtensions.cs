using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        services.AddScoped<IPublicationRepository, PostgresPublicationRepository>();
        services.AddScoped<ISystemSettingRepository, PostgresSystemSettingRepository>();
        services.AddScoped<IGenerationAttemptRepository, PostgresGenerationAttemptRepository>();
        services.AddScoped<IPostAuditRepository, PostgresPostAuditRepository>();

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

        // Presigned URL generation (RF-05): 24h validity, public HTTPS endpoint
        // Scoped: delegates to IObjectStoragePort, no shared state
        services.AddScoped<IPresignedUrlPort, PresignedUrlService>();

        // Token encryption (RF-03, ADR-006): AES-256-GCM for Meta token
        // Singleton: stateless, key is immutable
        services.AddSingleton<ITokenEncryptionPort, TokenEncryptionService>();

        // Token renewal (RF-03, BR-010): automatic refresh before 14 days
        // Scoped: uses repository and encryption service
        services.AddScoped<ITokenRenewalPort, TokenRenewalService>();

        // OpenRouter text generation (EP-02, F05): HttpClient for chat completions API
        services.AddHttpClient<ITextGenerationPort, OpenRouterTextGenerationAdapter>(client =>
        {
            client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        // OpenRouter image generation (EP-02, F06): HttpClient for image generation API
        services.AddHttpClient<IImageGenerationPort, OpenRouterImageGenerationAdapter>(client =>
        {
            client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        // Meta Graph API (EP-03, F10): HttpClient for Instagram publishing
        services.AddHttpClient<IInstagramPublishingPort, MetaInstagramPublishingAdapter>(client =>
        {
            client.BaseAddress = new Uri("https://graph.facebook.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        // Pipeline steps (RF-01)
        services.AddScoped<IPipelineStep, TextGenerationStep>();
        services.AddScoped<IPipelineStep, ImageGenerationStep>();
        services.AddScoped<IPipelineStep, PublicationStep>();

        return services;
    }
}