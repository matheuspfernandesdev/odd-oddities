using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// OpenRouter implementation of ITextGenerationPort.
/// Calls OpenRouter chat completions API to generate factual curiosity content.
/// Uses structured JSON response format for reliable parsing.
/// </summary>
public sealed class OpenRouterTextGenerationAdapter : ITextGenerationPort
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterConfiguration _config;
    private readonly ILogger<OpenRouterTextGenerationAdapter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterTextGenerationAdapter(
        HttpClient httpClient,
        IOptions<AppConfiguration> options,
        ILogger<OpenRouterTextGenerationAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = options?.Value?.OpenRouter ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(string TextContent, string Summary, string Theme, string SourceUrl, string Category, string Subcategory)> GenerateCuriosityAsync(
        string category,
        string subcategory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category cannot be null or empty.", nameof(category));

        if (string.IsNullOrWhiteSpace(subcategory))
            throw new ArgumentException("Subcategory cannot be null or empty.", nameof(subcategory));

        _logger.LogInformation(
            "Generating curiosity for {Category}/{Subcategory} using model {ModelId}",
            category,
            subcategory,
            _config.TextModelId);

        var request = new
        {
            model = _config.TextModelId,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You generate one factual curiosity in English. " +
                              "The content must be factual, not opinion-based, and not offensive (BR-001). " +
                              "Respond with a JSON object containing these exact fields: " +
                              "textContent (the curiosity, max 800 characters), " +
                              "summary (a short summary, max 500 characters), " +
                              "theme (a normalized theme label, max 120 characters), " +
                              "sourceUrl (a valid HTTP/HTTPS URL to a credible source), " +
                              "category (the category name), " +
                              "subcategory (the subcategory name)."
                },
                new
                {
                    role = "user",
                    content = $"Generate a curiosity about {category}/{subcategory}."
                }
            },
            response_format = new
            {
                type = "json_object"
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
        httpRequest.Headers.Add("HTTP-Referer", "https://odd-oddities.com");
        httpRequest.Headers.Add("X-Title", "Odd Oddities");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(
            JsonOptions, cancellationToken);

        if (responseBody?.Choices is null || responseBody.Choices.Length == 0)
        {
            throw new InvalidOperationException("OpenRouter returned an empty response.");
        }

        var contentJson = responseBody.Choices[0].Message.Content;

        _logger.LogDebug("Raw OpenRouter response: {Content}", contentJson);

        var curiosity = JsonSerializer.Deserialize<CuriosityResponse>(contentJson, JsonOptions);

        if (curiosity is null)
        {
            throw new InvalidOperationException("Failed to deserialize OpenRouter curiosity response.");
        }

        _logger.LogInformation(
            "Curiosity generated: TextLength={TextLength}, Theme={Theme}, SourceUrl={SourceUrl}",
            curiosity.TextContent?.Length ?? 0,
            curiosity.Theme,
            curiosity.SourceUrl);

        return (
            curiosity.TextContent ?? string.Empty,
            curiosity.Summary ?? string.Empty,
            curiosity.Theme ?? string.Empty,
            curiosity.SourceUrl ?? string.Empty,
            curiosity.Category ?? category,
            curiosity.Subcategory ?? subcategory
        );
    }

    private sealed class OpenRouterResponse
    {
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }

        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public MessageContent Message { get; set; } = new();
    }

    private sealed class MessageContent
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    private sealed class CuriosityResponse
    {
        [JsonPropertyName("textContent")]
        public string? TextContent { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("theme")]
        public string? Theme { get; set; }

        [JsonPropertyName("sourceUrl")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("subcategory")]
        public string? Subcategory { get; set; }
    }
}
