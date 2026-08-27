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

    Task<string> PublishMediaAsync(
        string creationId,
        CancellationToken cancellationToken = default);

    Task<(string Status, string StatusCode, string? Permalink)> GetMediaStatusAsync(
        string mediaId,
        CancellationToken cancellationToken = default);

    Task<(string NewToken, DateTime ExpiresAt)> RefreshAccessTokenAsync(
        string currentToken,
        CancellationToken cancellationToken = default);
}
