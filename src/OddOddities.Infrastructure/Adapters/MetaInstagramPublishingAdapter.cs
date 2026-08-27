using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// Meta Graph API implementation of IInstagramPublishingPort.
/// Handles media container creation, publishing, status polling, and token refresh
/// via the Meta Graph API (RF-01, RF-03).
/// </summary>
public sealed class MetaInstagramPublishingAdapter : IInstagramPublishingPort
{
    private readonly HttpClient _httpClient;
    private readonly MetaConfiguration _config;
    private readonly ILogger<MetaInstagramPublishingAdapter> _logger;

    private const string GraphApiVersion = "v17.0";
    private const string GraphApiBaseUrl = "https://graph.facebook.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MetaInstagramPublishingAdapter(
        HttpClient httpClient,
        IOptions<AppConfiguration> options,
        ILogger<MetaInstagramPublishingAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = options?.Value?.Meta ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> CreateMediaContainerAsync(
        string imageUrl,
        string caption,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new ArgumentException("Image URL cannot be null or empty.", nameof(imageUrl));

        _logger.LogInformation(
            "Creating media container for Instagram user {InstagramUserId}",
            _config.InstagramUserId);

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{_config.InstagramUserId}/media" +
                  $"?image_url={Uri.EscapeDataString(imageUrl)}" +
                  $"&caption={Uri.EscapeDataString(caption)}" +
                  $"&access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MediaContainerResponse>(
            JsonOptions, cancellationToken);

        if (string.IsNullOrEmpty(result?.Id))
        {
            throw new InvalidOperationException("Meta API returned an empty media container ID.");
        }

        _logger.LogInformation(
            "Media container created: mediaId={MediaId}",
            result.Id);

        return result.Id;
    }

    /// <inheritdoc />
    public async Task<string> PublishMediaAsync(
        string creationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(creationId))
            throw new ArgumentException("Creation ID cannot be null or empty.", nameof(creationId));

        _logger.LogInformation(
            "Publishing media container: creationId={CreationId}",
            creationId);

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{_config.InstagramUserId}/media_publish" +
                  $"?creation_id={Uri.EscapeDataString(creationId)}" +
                  $"&access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MediaContainerResponse>(
            JsonOptions, cancellationToken);

        if (string.IsNullOrEmpty(result?.Id))
        {
            throw new InvalidOperationException("Meta API returned an empty publish ID.");
        }

        _logger.LogInformation(
            "Media published: mediaId={MediaId}",
            result.Id);

        return result.Id;
    }

    /// <inheritdoc />
    public async Task<(string Status, string StatusCode, string? Permalink)> GetMediaStatusAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            throw new ArgumentException("Media ID cannot be null or empty.", nameof(mediaId));

        _logger.LogDebug(
            "Checking media status: mediaId={MediaId}",
            mediaId);

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{mediaId}" +
                  $"?fields=status_code,status_code_type,permalink" +
                  $"&access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MediaStatusResponse>(
            JsonOptions, cancellationToken);

        var status = result?.Status ?? "UNKNOWN";
        var statusCode = result?.StatusCode ?? "UNKNOWN";
        var permalink = result?.Permalink;

        _logger.LogDebug(
            "Media status: mediaId={MediaId}, status={Status}, statusCode={StatusCode}",
            mediaId,
            status,
            statusCode);

        return (status, statusCode, permalink);
    }

    /// <inheritdoc />
    public async Task<(string NewToken, DateTime ExpiresAt)> RefreshAccessTokenAsync(
        string currentToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentToken))
            throw new ArgumentException("Current token cannot be null or empty.", nameof(currentToken));

        _logger.LogInformation("Refreshing Meta access token");

        var url = $"https://graph.instagram.com/refresh_access_token" +
                  $"?grant_type=ig_refresh_token" +
                  $"&access_token={Uri.EscapeDataString(currentToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(
            JsonOptions, cancellationToken);

        if (string.IsNullOrEmpty(result?.AccessToken))
        {
            throw new InvalidOperationException("Meta API returned an empty access token.");
        }

        var expiresAt = DateTime.UtcNow.AddSeconds(result.ExpiresIn);

        _logger.LogInformation(
            "Token refreshed successfully: expiresAt={ExpiresAt:O}, expiresIn={ExpiresIn}s",
            expiresAt,
            result.ExpiresIn);

        return (result.AccessToken, expiresAt);
    }

    private sealed class MediaContainerResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class MediaStatusResponse
    {
        [JsonPropertyName("status_code")]
        public string? StatusCode { get; set; }

        [JsonPropertyName("status_code_type")]
        public string? Status { get; set; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class TokenRefreshResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }
    }
}
