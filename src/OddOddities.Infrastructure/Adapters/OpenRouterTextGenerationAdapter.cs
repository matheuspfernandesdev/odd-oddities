using System.Diagnostics;
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
/// OpenRouter implementation of ITextGenerationPort.
/// Calls OpenRouter chat completions API to generate factual curiosity content.
/// Uses structured JSON response format for reliable parsing. The model id is
/// provided by the caller so the pipeline can run a dynamic fallback chain.
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
    public async Task<TextGenerationResult> GenerateCuriosityAsync(
        string category,
        string subcategory,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category cannot be null or empty.", nameof(category));

        if (string.IsNullOrWhiteSpace(subcategory))
            throw new ArgumentException("Subcategory cannot be null or empty.", nameof(subcategory));

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("ModelId cannot be null or empty.", nameof(modelId));

        _logger.LogInformation(
            "Generating curiosity for {Category}/{Subcategory} using model {ModelId}",
            category,
            subcategory,
            modelId);

        var request = new
        {
            model = modelId,
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
                              "subcategory (the subcategory name). " +
                              "Return only a JSON object — not an array and not wrapped in markdown code fences."
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

        var stopwatch = Stopwatch.StartNew();
        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            stopwatch.Stop();
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter text generation failed for model {modelId}: {(int)response.StatusCode} {Truncate(errorBody, 300)}");
        }

        OpenRouterResponse? responseBody;
        try
        {
            responseBody = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(
                JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter text response for model {modelId} was not valid JSON.",
                ex);
        }

        stopwatch.Stop();

        if (responseBody?.Choices is null || responseBody.Choices.Length == 0)
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter returned an empty response for model {modelId}.");
        }

        var contentJson = responseBody.Choices[0].Message.Content;

        CuriosityPayload curiosity;

        try
        {
            curiosity = CuriosityJsonParser.Parse(contentJson);
        }
        catch (CuriosityParsingException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse curiosity JSON from model {ModelId}. Raw content: {RawContent}",
                modelId,
                contentJson);
            throw;
        }

        var costUsd = responseBody.Usage?.Cost;

        _logger.LogInformation(
            "Curiosity generated: ModelId={ModelId}, TextLength={TextLength}, Theme={Theme}, SourceUrl={SourceUrl}, CostUsd={CostUsd}, DurationMs={DurationMs}",
            modelId,
            curiosity.TextContent?.Length ?? 0,
            curiosity.Theme,
            curiosity.SourceUrl,
            costUsd,
            stopwatch.ElapsedMilliseconds);

        return new TextGenerationResult(
            TextContent: curiosity.TextContent ?? string.Empty,
            Summary: curiosity.Summary ?? string.Empty,
            Theme: curiosity.Theme ?? string.Empty,
            SourceUrl: curiosity.SourceUrl ?? string.Empty,
            Category: curiosity.Category ?? category,
            Subcategory: curiosity.Subcategory ?? subcategory,
            ModelId: modelId,
            CostUsd: costUsd,
            TokensIn: responseBody.Usage?.PromptTokens,
            TokensOut: responseBody.Usage?.CompletionTokens,
            DurationMs: stopwatch.ElapsedMilliseconds);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
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

        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }
    }
}
