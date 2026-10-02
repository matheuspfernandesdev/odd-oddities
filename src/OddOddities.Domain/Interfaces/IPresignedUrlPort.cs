namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for generating presigned URLs with a fixed 24-hour validity policy (RF-05).
/// Presigned URLs point to the public HTTPS endpoint (Nginx reverse proxy) and allow
/// the Meta Graph API to download images during publication. After 24 hours the URL
/// expires, but the underlying object remains in MinIO permanently (RF-04).
/// </summary>
public interface IPresignedUrlPort
{
    /// <summary>
    /// Generates a presigned URL for the given object key with a 24-hour validity.
    /// </summary>
    /// <param name="objectKey">The MinIO object key (UUID-based).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A fully-qualified HTTPS URL that expires in 24 hours.
    /// The URL points to the public Nginx reverse proxy endpoint.
    /// </returns>
    Task<string> GeneratePresignedUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
