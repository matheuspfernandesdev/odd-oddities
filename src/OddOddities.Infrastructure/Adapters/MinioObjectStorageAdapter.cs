using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// MinIO implementation of IObjectStoragePort using AWSSDK.S3 (RF-04).
/// Handles object upload with quota verification (BR-009), presigned URL generation,
/// and bucket usage tracking.
/// </summary>
public sealed class MinioObjectStorageAdapter : IObjectStoragePort, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonS3? _presignedClient;
    private readonly MinioConfiguration _config;
    private readonly ILogger<MinioObjectStorageAdapter> _logger;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initializes a new instance of <see cref="MinioObjectStorageAdapter"/> with a real S3 client.
    /// </summary>
    public MinioObjectStorageAdapter(
        IOptions<MinioConfiguration> options,
        ILogger<MinioObjectStorageAdapter> logger)
    {
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownsClient = true;

        _s3Client = CreateS3Client(_config);

        // A separate client for presigned URLs pointing to the public endpoint (ADR-005)
        if (!string.IsNullOrWhiteSpace(_config.PublicEndpoint))
        {
            _presignedClient = CreateS3ClientForPublicEndpoint(_config);
        }
    }

    /// <summary>
    /// Internal constructor for testing with a mocked S3 client.
    /// </summary>
    internal MinioObjectStorageAdapter(
        IAmazonS3 s3Client,
        IOptions<MinioConfiguration> options,
        ILogger<MinioObjectStorageAdapter> logger)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownsClient = false;
        _presignedClient = null;
    }

    /// <inheritdoc />
    public async Task PutObjectAsync(
        string key,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Object key cannot be null or empty.", nameof(key));

        if (data == null || data.Length == 0)
            throw new ArgumentException("Object data cannot be null or empty.", nameof(data));

        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type cannot be null or empty.", nameof(contentType));

        // BR-009: Verify quota before upload
        var currentUsage = await GetBucketUsageBytesAsync(cancellationToken);

        _logger.LogInformation(
            "Quota check before upload: key={Key}, size={Size:N0} bytes, " +
            "current={Current:N0} bytes, quota={Quota:N0} bytes",
            key,
            data.Length,
            currentUsage,
            _config.QuotaBytes);

        if (currentUsage + data.Length > _config.QuotaBytes)
        {
            _logger.LogError(
                "MinIO quota exceeded: attempted={Attempted:N0}, current={Current:N0}, quota={Quota:N0}",
                data.Length,
                currentUsage,
                _config.QuotaBytes);

            throw new QuotaExceededException(
                _config.QuotaBytes,
                currentUsage,
                data.Length);
        }

        // Ensure bucket exists
        await EnsureBucketExistsAsync(cancellationToken);

        // Upload object (dispose MemoryStream after upload to prevent memory leak)
        using var stream = new MemoryStream(data);
        var request = new PutObjectRequest
        {
            BucketName = _config.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        };

        var response = await _s3Client.PutObjectAsync(request, cancellationToken);

        _logger.LogInformation(
            "Object uploaded successfully: key={Key}, size={Size:N0} bytes, " +
            "httpStatusCode={StatusCode}",
            key,
            data.Length,
            response.HttpStatusCode);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Presigned URL generation is a local cryptographic operation (no network call),
    /// so <paramref name="cancellationToken"/> is accepted for API consistency but
    /// cannot be used to cancel the operation.
    /// </remarks>
    public Task<string> GeneratePresignedUrlAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Object key cannot be null or empty.", nameof(key));

        if (expiry <= TimeSpan.Zero)
            throw new ArgumentException("Expiry must be positive.", nameof(expiry));

        // ADR-005: Use the public endpoint client for presigned URLs.
        // This ensures the URL points to the Nginx reverse proxy with TLS.
        if (_presignedClient is null)
        {
            _logger.LogWarning(
                "PublicEndpoint is not configured. Presigned URL will use internal endpoint " +
                "which may not be accessible by external services like Meta Graph API. " +
                "Configure MinIO:PublicEndpoint in appsettings.json.");
        }

        var client = _presignedClient ?? _s3Client;

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _config.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = Protocol.HTTPS
        };

        var url = client.GetPreSignedURL(request);

        _logger.LogInformation(
            "Presigned URL generated: key={Key}, expiry={ExpiryHours}h",
            key,
            expiry.TotalHours);

        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public async Task<long> GetBucketUsageBytesAsync(CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _config.BucketName
        };

        long totalSize = 0;
        ListObjectsV2Response response;

        do
        {
            response = await _s3Client.ListObjectsV2Async(request, cancellationToken);
            totalSize += response.S3Objects.Sum(obj => obj.Size);
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated);

        _logger.LogDebug(
            "Bucket usage: {TotalSize:N0} bytes ({TotalSizeMB:N2} MB)",
            totalSize,
            totalSize / (1024.0 * 1024.0));

        return totalSize;
    }

    /// <summary>
    /// Ensures the configured bucket exists, creating it if necessary.
    /// </summary>
    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(
                _s3Client, _config.BucketName);

            if (!exists)
            {
                _logger.LogInformation(
                    "Creating bucket {BucketName}",
                    _config.BucketName);

                await _s3Client.PutBucketAsync(new PutBucketRequest
                {
                    BucketName = _config.BucketName,
                    UseClientRegion = true
                }, cancellationToken);

                _logger.LogInformation(
                    "Bucket {BucketName} created successfully",
                    _config.BucketName);
            }
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex,
                "Failed to ensure bucket {BucketName} exists: {ErrorMessage}",
                _config.BucketName,
                ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Creates an S3 client configured for MinIO compatibility (internal endpoint).
    /// </summary>
    private static IAmazonS3 CreateS3Client(MinioConfiguration config)
    {
        var s3Config = new AmazonS3Config
        {
            // MinIO internal endpoint configuration
            ServiceURL = $"http://{config.Endpoint}",
            // Force path-style URLs (required for MinIO)
            ForcePathStyle = true,
            // Use HTTP for internal Docker communication
            UseHttp = true,
            // Increase timeout for large uploads
            Timeout = TimeSpan.FromMinutes(10),
            // Retry configuration
            MaxErrorRetry = 3
        };

        return new AmazonS3Client(
            config.AccessKey,
            config.SecretKey,
            s3Config);
    }

    /// <summary>
    /// Creates an S3 client configured for the public endpoint (Nginx reverse proxy with TLS).
    /// Used exclusively for generating presigned URLs that must be publicly accessible (ADR-005).
    /// </summary>
    private static IAmazonS3 CreateS3ClientForPublicEndpoint(MinioConfiguration config)
    {
        var publicUrl = config.PublicEndpoint;

        // Ensure the URL has a scheme
        if (!publicUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            publicUrl = $"https://{publicUrl}";
        }

        var s3Config = new AmazonS3Config
        {
            ServiceURL = publicUrl,
            ForcePathStyle = true,
            Timeout = TimeSpan.FromMinutes(5),
            MaxErrorRetry = 3
        };

        return new AmazonS3Client(
            config.AccessKey,
            config.SecretKey,
            s3Config);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _s3Client?.Dispose();
            _presignedClient?.Dispose();
        }
    }
}
