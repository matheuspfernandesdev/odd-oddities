namespace OddOddities.Domain.Constants;

/// <summary>
/// Pipeline-related constants. Centralized so that magic numbers don't drift
/// across steps and orchestrator.
/// </summary>
public static class PipelineConstants
{
    /// <summary>
    /// Maximum number of text generation attempts per pipeline run (BR-006).
    /// </summary>
    public const int MaxGenerationAttempts = 3;

    /// <summary>
    /// Maximum number of polling attempts when checking Instagram publication status.
    /// </summary>
    public const int MaxPollingAttempts = 30;

    /// <summary>
    /// Polling interval in seconds between Instagram status checks.
    /// </summary>
    public const int PollingIntervalSeconds = 2;

    /// <summary>
    /// Default similarity threshold for textual content (BR-005).
    /// </summary>
    public const double DefaultSimilarityThreshold = 0.80;

    /// <summary>
    /// Window in days for the "least used category" rotation (RF-06).
    /// </summary>
    public const int DefaultCategoryRotationWindowDays = 90;

    /// <summary>
    /// Window in days for ContentHash duplicate detection (BR-004).
    /// </summary>
    public const int DuplicateDetectionWindowDays = 90;

    /// <summary>
    /// Window in days for similarity search (BR-005).
    /// </summary>
    public const int SimilaritySearchWindowDays = 90;
}

/// <summary>
/// Storage / object-storage constants.
/// </summary>
public static class StorageConstants
{
    /// <summary>
    /// Default MinIO quota in bytes (BR-009). 20 GB.
    /// </summary>
    public const long MinioDefaultQuotaBytes = 21_474_836_480L;

    /// <summary>
    /// Presigned URL validity in hours (RF-05).
    /// </summary>
    public const int PresignedUrlExpiryHours = 24;
}

/// <summary>
/// Token / Meta API constants.
/// </summary>
public static class TokenConstants
{
    /// <summary>
    /// Threshold in days before expiry to trigger Meta token renewal (BR-010).
    /// </summary>
    public const int RenewalThresholdDays = 14;

    /// <summary>
    /// SystemSetting key for the encrypted Meta access token.
    /// </summary>
    public const string MetaTokenKey = "META_ACCESS_TOKEN";

    /// <summary>
    /// SystemSetting key for the Meta token expiry date.
    /// </summary>
    public const string MetaTokenExpiresAtKey = "META_TOKEN_EXPIRES_AT";
}

/// <summary>
/// Similarity computation constants.
/// </summary>
public static class SimilarityConstants
{
    /// <summary>
    /// Minimum token length considered for Jaccard similarity (ignore words shorter than 3 chars).
    /// </summary>
    public const int MinTokenLength = 3;
}
