using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// OpenRouter implementation of IImageGenerationPort.
/// Calls OpenRouter image generation API to create artistic illustrations.
/// Returns raw PNG image bytes for downstream processing.
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
    public async Task<byte[]> GenerateImageAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

        _logger.LogInformation(
            "Generating image using model {ModelId}, prompt length={PromptLength}",
            _config.ImageModelId,
            prompt.Length);

        var request = new
        {
            model = _config.ImageModelId,
            prompt = $"A poetic surreal illustration about {prompt}. " +
                     "Artistic, dreamlike quality, suitable for Instagram. " +
                     "No text or watermarks in the image."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "images/generations")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        httpRequest.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
        httpRequest.Headers.Add("HTTP-Referer", "https://odd-oddities.com");
        httpRequest.Headers.Add("X-Title", "Odd Oddities");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadFromJsonAsync<ImageGenerationResponse>(
            JsonOptions, cancellationToken);

        if (responseBody?.Data is null || responseBody.Data.Length == 0)
        {
            throw new InvalidOperationException("OpenRouter returned an empty image response.");
        }

        var imageData = responseBody.Data[0];

        byte[] imageBytes;

        if (!string.IsNullOrEmpty(imageData.B64Json))
        {
            // Response contains base64-encoded image
            imageBytes = Convert.FromBase64String(imageData.B64Json);
            _logger.LogInformation(
                "Image generated from base64: {SizeBytes} bytes",
                imageBytes.Length);
        }
        else if (!string.IsNullOrEmpty(imageData.Url))
        {
            // Response contains a URL to download the image
            _logger.LogInformation("Downloading image from URL: {Url}", imageData.Url);
            imageBytes = await _httpClient.GetByteArrayAsync(imageData.Url, cancellationToken);
            _logger.LogInformation(
                "Image downloaded: {SizeBytes} bytes",
                imageBytes.Length);
        }
        else
        {
            throw new InvalidOperationException(
                "OpenRouter image response contains neither base64 data nor URL.");
        }

        return imageBytes;
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
        public decimal Cost { get; set; }
    }
}
