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
/// OpenRouter implementation of ICommentClassificationPort (RF-20).
/// Sends every new comment to a single chat completion (JSON response format) asking
/// whether the comment suggests a factual theme adequate for the Odd Oddities profile
/// and, when it does, extracting a normalized {theme, summary}. The model id comes from
/// the caller so classification rides the existing free text chain.
/// </summary>
public sealed class OpenRouterCommentClassificationAdapter : ICommentClassificationPort
{
    private const string SystemPrompt =
        "You classify Instagram comments for an account that posts one unusual factual curiosity per post " +
        "(science, nature, history, technology). " +
        "For each comment decide whether it suggests a factual topic/theme that would fit this profile: " +
        "a concrete, verifiable subject worth a future curiosity post. " +
        "It is NOT a suggestion when the comment is a question about the account, a compliment, spam, " +
        "an opinion, a personal request, or a topic that is not factual. " +
        "When it is a suggestion, extract a normalized theme (max 120 characters) and a short summary " +
        "(max 500 characters) describing what the suggested post would be about. " +
        "Respond with a JSON object containing a single field \"results\": an array with one entry per input " +
        "comment, each entry having exactly these fields: " +
        "commentId (the input comment id), " +
        "isSuggestion (boolean), " +
        "theme (string, empty when isSuggestion is false), " +
        "summary (string, empty when isSuggestion is false). " +
        "Return only a JSON object — not an array and not wrapped in markdown code fences.";

    private readonly HttpClient _httpClient;
    private readonly OpenRouterConfiguration _config;
    private readonly ILogger<OpenRouterCommentClassificationAdapter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterCommentClassificationAdapter(
        HttpClient httpClient,
        IOptions<AppConfiguration> options,
        ILogger<OpenRouterCommentClassificationAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = options?.Value?.OpenRouter ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommentClassificationResult>> ClassifyCommentsAsync(
        IReadOnlyList<MediaComment> comments,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (comments is null || comments.Count == 0)
            throw new ArgumentException("Comments cannot be null or empty.", nameof(comments));

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("ModelId cannot be null or empty.", nameof(modelId));

        _logger.LogInformation(
            "Classifying {CommentCount} comments using model {ModelId}",
            comments.Count,
            modelId);

        var request = new
        {
            model = modelId,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = SystemPrompt
                },
                new
                {
                    role = "user",
                    content = BuildUserPrompt(comments)
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
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            stopwatch.Stop();
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter comment classification failed for model {modelId}: {(int)response.StatusCode} {Truncate(errorBody, 300)}");
        }

        ClassificationPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<ClassificationPayload>(
                JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter comment classification response for model {modelId} was not valid JSON.",
                ex);
        }

        stopwatch.Stop();

        if (payload?.Results is null || payload.Results.Count == 0)
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter returned an empty comment classification payload for model {modelId}.");
        }

        var results = payload.Results
            .Where(entry => !string.IsNullOrWhiteSpace(entry.CommentId))
            .Select(entry => new CommentClassificationResult(
                CommentId: entry.CommentId!,
                IsSuggestion: entry.IsSuggestion,
                Theme: entry.Theme ?? string.Empty,
                Summary: entry.Summary ?? string.Empty))
            .ToList()
            .AsReadOnly();

        _logger.LogInformation(
            "Comment classification completed: ModelId={ModelId}, Results={ResultCount}, Suggestions={SuggestionCount}, CostUsd={CostUsd}, DurationMs={DurationMs}",
            modelId,
            results.Count,
            results.Count(r => r.IsSuggestion),
            payload.Usage?.Cost,
            stopwatch.ElapsedMilliseconds);

        return results;
    }

    /// <summary>
    /// One numbered line per comment. Ids are echoed back by the model, which is how the
    /// step matches classifications to comments.
    /// </summary>
    private static string BuildUserPrompt(IReadOnlyList<MediaComment> comments)
    {
        var lines = new List<string>
        {
            "Classify every comment below and return the \"results\" array with one entry per comment id:",
            string.Empty
        };

        for (var i = 0; i < comments.Count; i++)
        {
            lines.Add($"{i + 1}. [id={comments[i].CommentId}] {comments[i].Text}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed class ClassificationPayload
    {
        [JsonPropertyName("results")]
        public List<ClassificationEntry>? Results { get; set; }

        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; set; }
    }

    private sealed class UsageInfo
    {
        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }
    }

    private sealed class ClassificationEntry
    {
        [JsonPropertyName("commentId")]
        public string? CommentId { get; set; }

        [JsonPropertyName("isSuggestion")]
        public bool IsSuggestion { get; set; }

        [JsonPropertyName("theme")]
        public string? Theme { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }
}
