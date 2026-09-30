using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// Meta Graph API implementation of IInstagramPublishingPort and IMediaCommentPort.
/// Handles media and Reels container creation, publishing, status polling, token
/// refresh, and comment reading/replying via the Meta Graph API (RF-01, RF-03,
/// RF-18, RF-19).
/// </summary>
public sealed class MetaInstagramPublishingAdapter : IInstagramPublishingPort, IMediaCommentPort
{
    private readonly HttpClient _httpClient;
    private readonly MetaConfiguration _config;
    private readonly ILogger<MetaInstagramPublishingAdapter> _logger;

    private const string GraphApiVersion = "v26.0";
    private const string GraphApiBaseUrl = "https://graph.instagram.com";

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
        await EnsureSuccessAsync(response, "media", cancellationToken);

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
    public async Task<string> CreateReelsContainerAsync(
        string videoUrl,
        string caption,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
            throw new ArgumentException("Video URL cannot be null or empty.", nameof(videoUrl));

        _logger.LogInformation(
            "Creating Reels media container for Instagram user {InstagramUserId}",
            _config.InstagramUserId);

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{_config.InstagramUserId}/media" +
                  $"?media_type=REELS" +
                  $"&video_url={Uri.EscapeDataString(videoUrl)}" +
                  $"&caption={Uri.EscapeDataString(caption)}" +
                  $"&access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Reels media", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<MediaContainerResponse>(
            JsonOptions, cancellationToken);

        if (string.IsNullOrEmpty(result?.Id))
        {
            throw new InvalidOperationException("Meta API returned an empty Reels media container ID.");
        }

        _logger.LogInformation(
            "Reels media container created: mediaId={MediaId}",
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
        await EnsureSuccessAsync(response, "media_publish", cancellationToken);

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
    public async Task<string> GetContainerStatusAsync(
        string containerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(containerId));

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{containerId}" +
                  $"?fields=status_code" +
                  $"&access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "container status", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<MediaStatusResponse>(
            JsonOptions, cancellationToken);

        var statusCode = result?.StatusCode ?? "UNKNOWN";

        _logger.LogDebug(
            "Media container status: containerId={ContainerId}, statusCode={StatusCode}",
            containerId,
            statusCode);

        return statusCode;
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
                  $"?fields=status_code,permalink" +
                  $"&access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "media status", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<MediaStatusResponse>(
            JsonOptions, cancellationToken);

        var permalink = result?.Permalink;
        var statusCode = result?.StatusCode ?? "UNKNOWN";
        var status = statusCode switch
        {
            "ERROR" => "ERROR",
            "EXPIRED" => "EXPIRED",
            _ when !string.IsNullOrEmpty(permalink) => "PUBLISHED",
            _ => "PENDING"
        };

        _logger.LogDebug(
            "Media status: mediaId={MediaId}, status={Status}, statusCode={StatusCode}, permalink={Permalink}",
            mediaId,
            status,
            statusCode,
            permalink);

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
        await EnsureSuccessAsync(response, "refresh_access_token", cancellationToken);

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaComment>> GetCommentsAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            throw new ArgumentException("Media ID cannot be null or empty.", nameof(mediaId));

        _logger.LogDebug("Fetching comments for media {MediaId}", mediaId);

        var comments = new List<MediaComment>();
        string? after = null;

        // The endpoint returns up to 50 top-level comments per call; keep following the
        // "after" cursor until the API reports no further page (RF correction 8.1 item 5).
        while (true)
        {
            var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{mediaId}/comments" +
                      $"?limit=50" +
                      $"&fields=id,text,timestamp,from.username" +
                      $"&access_token={Uri.EscapeDataString(_config.AccessToken)}" +
                      (after is null ? string.Empty : $"&after={Uri.EscapeDataString(after)}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Dispose per iteration: the pagination loop can issue many requests per
            // media, and an undisposed response keeps its connection out of the pool
            // until finalization.
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "comments", cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<CommentsPageResponse>(
                JsonOptions, cancellationToken);

            var pageData = page?.Data;
            if (pageData is not { Count: > 0 })
            {
                break; // exhausted
            }

            comments.AddRange(pageData.Select(MapComment));

            var nextAfter = page?.Paging?.Cursors?.After;
            if (string.IsNullOrEmpty(nextAfter) || nextAfter == after)
            {
                break; // last page (or cursor did not advance)
            }

            after = nextAfter;
        }

        _logger.LogDebug(
            "Fetched {CommentCount} comments for media {MediaId}",
            comments.Count,
            mediaId);

        return comments.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task ReplyToCommentAsync(
        string commentId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commentId))
            throw new ArgumentException("Comment ID cannot be null or empty.", nameof(commentId));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Reply message cannot be null or empty.", nameof(message));

        _logger.LogInformation("Replying to Instagram comment {CommentId}", commentId);

        var url = $"{GraphApiBaseUrl}/{GraphApiVersion}/{commentId}/replies" +
                  $"?access_token={Uri.EscapeDataString(_config.AccessToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["message"] = message
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "comment reply", cancellationToken);

        _logger.LogInformation("Replied to Instagram comment {CommentId}", commentId);
    }

    /// <summary>
    /// Maps one comment payload from the Graph API (id, text, timestamp, from.username).
    /// An unparseable timestamp becomes DateTime.MinValue (UTC) so client-side
    /// "newer than last run" filters never discard the comment — idempotency handles it.
    /// </summary>
    private static MediaComment MapComment(CommentsPageData data) => new(
        CommentId: data.Id ?? string.Empty,
        Text: data.Text ?? string.Empty,
        Timestamp: ParseTimestamp(data.Timestamp),
        AuthorUsername: data.From?.Username ?? string.Empty);

    private static DateTime ParseTimestamp(string? raw)
    {
        if (DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
    }

    /// <summary>
    /// Throws an <see cref="HttpRequestException"/> carrying the Meta error body, so the
    /// failure reason stored on the Post explains why the API rejected the call.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new HttpRequestException(
            $"Meta {operation} failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}",
            null,
            response.StatusCode);
    }

    private sealed class MediaContainerResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class MediaStatusResponse
    {
        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("status_code")]
        public string? StatusCode { get; set; }

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

    private sealed class CommentsPageResponse
    {
        [JsonPropertyName("data")]
        public List<CommentsPageData>? Data { get; set; }

        [JsonPropertyName("paging")]
        public CommentsPaging? Paging { get; set; }
    }

    private sealed class CommentsPageData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("from")]
        public CommentAuthor? From { get; set; }
    }

    private sealed class CommentAuthor
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }

    private sealed class CommentsPaging
    {
        [JsonPropertyName("cursors")]
        public CommentCursors? Cursors { get; set; }
    }

    private sealed class CommentCursors
    {
        [JsonPropertyName("after")]
        public string? After { get; set; }
    }
}
