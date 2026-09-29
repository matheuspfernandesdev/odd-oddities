using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// OpenRouter implementation of IModelCatalogPort.
/// Queries GET /models (text) and GET /models?output_modalities=image (image) to build
/// dynamic fallback chains. Pricing fields are USD strings; "-1" (dynamic router pricing)
/// is normalized to null.
/// </summary>
public sealed class OpenRouterModelCatalogAdapter : IModelCatalogPort
{
    private const string TextModelsPath = "models?output_modalities=text&sort=pricing-low-to-high";
    private const string ImageModelsPath = "models?output_modalities=image";

    private readonly HttpClient _httpClient;
    private readonly OpenRouterConfiguration _config;
    private readonly ILogger<OpenRouterModelCatalogAdapter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OpenRouterModelCatalogAdapter(
        HttpClient httpClient,
        IOptions<AppConfiguration> options,
        ILogger<OpenRouterModelCatalogAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = options?.Value?.OpenRouter ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelDescriptor>> GetTextModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await FetchAsync(TextModelsPath, cancellationToken);

        return models
            .Where(m => m.OutputsText)
            .OrderByDescending(m => m.IsFree)
            .ThenBy(m => EffectiveTextPrice(m))
            .ThenByDescending(m => m.Created)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelDescriptor>> GetImageModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await FetchAsync(ImageModelsPath, cancellationToken);

        return models
            .Where(m => m.OutputsImage && (m.IsFree || m.ImageOutputPrice is not null))
            .OrderByDescending(m => m.IsFree)
            .ThenBy(m => m.ImageOutputPrice ?? decimal.MaxValue)
            .ThenByDescending(m => m.Created)
            .ToList();
    }

    private async Task<List<ModelDescriptor>> FetchAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (!string.IsNullOrEmpty(_config.ApiKey))
            request.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");

        request.Headers.Add("HTTP-Referer", "https://odd-oddities.com");
        request.Headers.Add("X-Title", "Odd Oddities");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenRouterModelException(
                modelId: "(catalog)",
                statusCode: (int)response.StatusCode,
                message: $"Failed to fetch OpenRouter model catalog: {(int)response.StatusCode} {Truncate(body, 300)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ModelsResponse>(JsonOptions, cancellationToken);

        if (payload?.Data is null)
        {
            throw new OpenRouterModelException(
                modelId: "(catalog)",
                statusCode: null,
                message: "OpenRouter model catalog returned an empty payload.");
        }

        var descriptors = payload.Data
            .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !m.Id.StartsWith('~'))
            .Select(Map)
            .ToList();

        _logger.LogInformation("Fetched {Count} models from OpenRouter catalog", descriptors.Count);
        return descriptors;
    }

    private static decimal EffectiveTextPrice(ModelDescriptor m)
    {
        if (m.PromptPricePerToken is null && m.CompletionPricePerToken is null)
            return decimal.MaxValue;

        return (m.PromptPricePerToken ?? 0) + (m.CompletionPricePerToken ?? 0);
    }

    private static ModelDescriptor Map(ModelDto dto)
    {
        var id = dto.Id ?? string.Empty;
        var prompt = ParsePrice(dto.Pricing?.Prompt);
        var completion = ParsePrice(dto.Pricing?.Completion);
        var imageOutput = ParsePrice(dto.Pricing?.ImageOutput);

        var isFree = id.EndsWith(":free", StringComparison.OrdinalIgnoreCase)
            || (prompt == 0 && completion == 0 && imageOutput is null or 0);

        return new ModelDescriptor(
            Id: id,
            Name: dto.Name ?? id,
            PromptPricePerToken: prompt,
            CompletionPricePerToken: completion,
            ImageOutputPrice: imageOutput,
            IsFree: isFree,
            ContextLength: dto.ContextLength,
            Created: dto.Created,
            OutputModalities: dto.Architecture?.OutputModalities);
    }

    /// <summary>Parses an OpenRouter USD price string. "-1" (dynamic) and unparsable → null.</summary>
    private static decimal? ParsePrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return null;

        // Sentinel: dynamic/router pricing (e.g. openrouter/auto).
        if (parsed < 0)
            return null;

        return parsed;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelDto>? Data { get; set; }

        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }
    }

    private sealed class ModelDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("context_length")]
        public int? ContextLength { get; set; }

        [JsonPropertyName("architecture")]
        public ArchitectureDto? Architecture { get; set; }

        [JsonPropertyName("pricing")]
        public PricingDto? Pricing { get; set; }
    }

    private sealed class ArchitectureDto
    {
        [JsonPropertyName("output_modalities")]
        public List<string>? OutputModalities { get; set; }
    }

    private sealed class PricingDto
    {
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("completion")]
        public string? Completion { get; set; }

        [JsonPropertyName("image_output")]
        public string? ImageOutput { get; set; }
    }
}
