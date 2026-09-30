namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for publishing to Instagram via Meta Graph API.
/// </summary>
public interface IInstagramPublishingPort
{
    Task<string> CreateMediaContainerAsync(
        string imageUrl,
        string caption,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Reels media container (media_type=REELS) for a video URL. The URL must be
    /// publicly accessible because Meta downloads the video from it (RF-18).
    /// </summary>
    Task<string> CreateReelsContainerAsync(
        string videoUrl,
        string caption,
        CancellationToken cancellationToken = default);

    Task<string> PublishMediaAsync(
        string creationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the processing status_code of a media container (FINISHED/ERROR/EXPIRED/PROCESSING).
    /// The container must reach FINISHED before it can be published.
    /// </summary>
    Task<string> GetContainerStatusAsync(
        string containerId,
        CancellationToken cancellationToken = default);

    Task<(string Status, string StatusCode, string? Permalink)> GetMediaStatusAsync(
        string mediaId,
        CancellationToken cancellationToken = default);

    Task<(string NewToken, DateTime ExpiresAt)> RefreshAccessTokenAsync(
        string currentToken,
        CancellationToken cancellationToken = default);
}
