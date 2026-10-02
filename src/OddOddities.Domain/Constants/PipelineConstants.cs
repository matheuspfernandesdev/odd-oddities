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
    /// Initial delay in seconds before retrying a transient API error (ADR-007).
    /// </summary>
    public const int RetryBaseDelaySeconds = 10;

    /// <summary>
    /// Exponential backoff multiplier applied per attempt (ADR-007).
    /// </summary>
    public const int RetryMultiplier = 2;

    /// <summary>
    /// Maximum delay in seconds between retries (ADR-007).
    /// </summary>
    public const int RetryMaxDelaySeconds = 120;

    /// <summary>
    /// Maximum number of attempts to wait for the Instagram media container to reach
    /// FINISHED before publishing. Publishing a container that is still processing
    /// makes the Meta API reject the media_publish call.
    /// </summary>
    public const int MaxContainerPollingAttempts = 15;

    /// <summary>
    /// Maximum number of attempts to read the permalink of an already published media.
    /// Best effort: a missing permalink does not fail the publication.
    /// </summary>
    public const int MaxPermalinkPollingAttempts = 6;

    /// <summary>
    /// Hard upper bound (in seconds) for the whole publication step, so a stalled Meta
    /// API call can never block the pipeline indefinitely.
    /// </summary>
    public const int PublicationStepTimeoutSeconds = 240;

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

    /// <summary>
    /// Maximum total length of the final Instagram caption (content + source + credit).
    /// Above this limit the Meta API rejects the publication (R-04), so the pipeline
    /// rejects the generated text with CAPTION_TOO_LONG before creating the Post.
    /// </summary>
    public const int InstagramMaxCaptionLength = 2200;

    /// <summary>
    /// Estimated prompt tokens used to estimate text generation cost before the call
    /// (candidate budget filtering). Accounts for the editorial brief in the system prompt.
    /// </summary>
    public const int EstimatedPromptTokens = 2000;

    /// <summary>
    /// Estimated completion tokens used to estimate text generation cost before the call.
    /// Accounts for captions up to AppConfiguration.MaxCaptionContentLength (1600).
    /// </summary>
    public const int EstimatedCompletionTokens = 1200;
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
