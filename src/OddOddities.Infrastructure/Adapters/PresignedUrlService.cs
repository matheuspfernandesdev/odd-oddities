using Microsoft.Extensions.Logging;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// Application service that generates presigned URLs with a fixed 24-hour validity policy (RF-05).
/// Delegates to <see cref="IObjectStoragePort"/> which uses the public HTTPS endpoint (Nginx)
/// for the presigned URL generation, ensuring Meta Graph API can download the image during publication.
/// </summary>
public sealed class PresignedUrlService : IPresignedUrlPort
{
    private readonly IObjectStoragePort _storagePort;
    private readonly ILogger<PresignedUrlService> _logger;

    private static readonly TimeSpan PresignedUrlExpiry = TimeSpan.FromHours(StorageConstants.PresignedUrlExpiryHours);

    public PresignedUrlService(
        IObjectStoragePort storagePort,
        ILogger<PresignedUrlService> logger)
    {
        _storagePort = storagePort ?? throw new ArgumentNullException(nameof(storagePort));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GeneratePresignedUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Object key cannot be null or empty.", nameof(objectKey));

        _logger.LogInformation(
            "Generating presigned URL: key={Key}, expiry={ExpiryHours}h",
            objectKey,
            PresignedUrlExpiry.TotalHours);

        var url = await _storagePort.GeneratePresignedUrlAsync(
            objectKey,
            PresignedUrlExpiry,
            cancellationToken);

        _logger.LogInformation(
            "Presigned URL generated successfully: key={Key}",
            objectKey);

        return url;
    }
}
