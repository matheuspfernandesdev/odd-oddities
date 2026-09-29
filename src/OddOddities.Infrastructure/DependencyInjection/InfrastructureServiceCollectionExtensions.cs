using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OddOddities.Application.Abstractions;
using OddOddities.Application.Ports;
using OddOddities.Application.UseCases;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;
using OddOddities.Infrastructure.Adapters;
using OddOddities.Infrastructure.Logging;

namespace OddOddities.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Infrastructure layer services (repositories, adapters).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IPostRepository, PostgresPostRepository>();
        services.AddScoped<IPublicationRepository, PostgresPublicationRepository>();
        services.AddScoped<ISystemSettingRepository, PostgresSystemSettingRepository>();
        services.AddScoped<IGenerationAttemptRepository, PostgresGenerationAttemptRepository>();
        services.AddScoped<IPostAuditRepository, PostgresPostAuditRepository>();

        services.AddSingleton<ISchedulerPort, ScheduleService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ILogCorrelationPort, LogCorrelationService>();

        services.AddHttpClient(nameof(SourceValidationService), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0 (SourceValidator)");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            UseDefaultCredentials = false
        });

        services.AddScoped<ISourceValidationPort, SourceValidationService>();
        services.AddScoped<ISimilarityCheckPort, SimilarityCheckService>();
        services.AddScoped<IImageProcessingPort, ImageSharpProcessingService>();
        services.AddScoped<IObjectStoragePort, MinioObjectStorageAdapter>();
        services.AddScoped<IPresignedUrlPort, PresignedUrlService>();
        services.AddSingleton<ITokenEncryptionPort, TokenEncryptionService>();
        services.AddScoped<ITokenRenewalPort, TokenRenewalService>();

        services.AddHttpClient<ITextGenerationPort, OpenRouterTextGenerationAdapter>((sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<AppConfiguration>>().Value.OpenRouter;
            client.BaseAddress = new Uri(EnsureTrailingSlash(config.BaseUrl));
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        services.AddHttpClient<IImageGenerationPort, OpenRouterImageGenerationAdapter>((sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<AppConfiguration>>().Value.OpenRouter;
            client.BaseAddress = new Uri(EnsureTrailingSlash(config.BaseUrl));
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        services.AddHttpClient<IModelCatalogPort, OpenRouterModelCatalogAdapter>((sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<AppConfiguration>>().Value.OpenRouter;
            client.BaseAddress = new Uri(EnsureTrailingSlash(config.BaseUrl));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        services.AddHttpClient<IInstagramPublishingPort, MetaInstagramPublishingAdapter>(client =>
        {
            client.BaseAddress = new Uri("https://graph.instagram.com");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "OddOddities/1.0");
        });

        return services;
    }

    private static string EnsureTrailingSlash(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://openrouter.ai/api/v1";

        return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }
}
