namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for object storage operations (MinIO/S3 compatible).
/// </summary>
public interface IObjectStoragePort
{
    Task PutObjectAsync(
        string key,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<string> GeneratePresignedUrlAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task<long> GetBucketUsageBytesAsync(
        CancellationToken cancellationToken = default);
}
