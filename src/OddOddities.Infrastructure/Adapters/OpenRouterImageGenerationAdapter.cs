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
/// OpenRouter implementation of IImageGenerationPort.
/// Calls POST /api/v1/images to create artistic illustrations.
/// Returns raw image bytes for downstream processing. The model id is provided
/// by the caller so the pipeline can run a dynamic fallback chain.
/// </summary>
public sealed class OpenRouterImageGenerationAdapter : IImageGenerationPort
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterConfiguration _config;
    private readonly ILogger<OpenRouterImageGenerationAdapter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterImageGenerationAdapter(
        HttpClient httpClient,
        IOptions<AppConfiguration> options,
        ILogger<OpenRouterImageGenerationAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = options?.Value?.OpenRouter ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResult> GenerateImageAsync(
        string prompt,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("ModelId cannot be null or empty.", nameof(modelId));

        _logger.LogInformation(
            "Generating image using model {ModelId}, prompt length={PromptLength}",
            modelId,
            prompt.Length);

        var request = new
        {
            model = modelId,
            prompt = $"A poetic surreal illustration whose clear, unmistakable focal point is: {prompt}. " +
                     "Render this subject with concrete, recognizable detail so it reads instantly as the " +
                     "main point of the image. " +
                     "Surround it with a rich, coherent setting and supporting visual elements that make " +
                     "narrative sense with the subject — an evocative environment, background and complementary " +
                     "details that add depth and context without competing with or obscuring the focal point. " +
                     "Artistic, dreamlike quality, suitable for Instagram. " +
                     "No text, letters, numbers or watermarks in the image."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "images")
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
                $"OpenRouter image generation failed for model {modelId}: {(int)response.StatusCode} {Truncate(errorBody, 300)}");
        }

        ImageGenerationResponse? responseBody;
        try
        {
            responseBody = await response.Content.ReadFromJsonAsync<ImageGenerationResponse>(
                JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter image response for model {modelId} was not valid JSON.",
                ex);
        }

        if (responseBody?.Data is null || responseBody.Data.Length == 0)
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter returned an empty image response for model {modelId}.");
        }

        var imageData = responseBody.Data[0];

        byte[] imageBytes;

        if (!string.IsNullOrEmpty(imageData.B64Json))
        {
            imageBytes = Convert.FromBase64String(imageData.B64Json);
            _logger.LogInformation(
                "Image generated from base64: {SizeBytes} bytes",
                imageBytes.Length);
        }
        else if (!string.IsNullOrEmpty(imageData.Url))
        {
            _logger.LogInformation("Downloading image from URL: {Url}", imageData.Url);
            imageBytes = await _httpClient.GetByteArrayAsync(imageData.Url, cancellationToken);
            _logger.LogInformation(
                "Image downloaded: {SizeBytes} bytes",
                imageBytes.Length);
        }
        else
        {
            throw new OpenRouterModelException(
                modelId,
                (int)response.StatusCode,
                $"OpenRouter image response for model {modelId} contains neither base64 data nor URL.");
        }

        stopwatch.Stop();

        var costUsd = responseBody.Usage?.Cost;

        _logger.LogInformation(
            "Image generated: ModelId={ModelId}, SizeBytes={SizeBytes}, CostUsd={CostUsd}, DurationMs={DurationMs}",
            modelId,
            imageBytes.Length,
            costUsd,
            stopwatch.ElapsedMilliseconds);

        return new ImageGenerationResult(
            ImageBytes: imageBytes,
            ModelId: modelId,
            CostUsd: costUsd,
            DurationMs: stopwatch.ElapsedMilliseconds);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed class ImageGenerationResponse
    {
        [JsonPropertyName("data")]
        public ImageData[]? Data { get; set; }

        [JsonPropertyName("usage")]
        public ImageUsageInfo? Usage { get; set; }
    }

    private sealed class ImageData
    {
        [JsonPropertyName("b64_json")]
        public string? B64Json { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class ImageUsageInfo
    {
        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }
    }
}
