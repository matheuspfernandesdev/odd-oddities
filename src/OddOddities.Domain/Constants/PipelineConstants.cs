namespace OddOddities.Domain.Constants;

/// <summary>
/// Centralized constants used by the content pipeline.
/// Values are extracted (not changed) from their original locations to provide
/// a single, predictable place for pipeline-wide tuning parameters.
/// </summary>
public static class PipelineConstants
{
    /// <summary>
    /// Maximum number of text generation attempts per pipeline execution (BR-006).
    /// </summary>
    public const int MaxGenerationAttempts = 3;

    /// <summary>
    /// Maximum number of polling attempts when waiting for Meta to publish media.
    /// </summary>
    public const int MaxPollingAttempts = 30;

    /// <summary>
    /// Interval in seconds between Meta media status polls.
    /// </summary>
    public const int PollingIntervalSeconds = 2;

    /// <summary>
    /// Threshold in days before Meta token expiry that triggers renewal (BR-010).
    /// </summary>
    public const int RenewalThresholdDays = 14;

    /// <summary>
    /// Default Jaccard similarity threshold for textual duplicate detection (BR-005).
    /// </summary>
    public const double DefaultSimilarityThreshold = 0.80;

    /// <summary>
    /// Window in days used to look back for similar/duplicate content (BR-004, BR-005).
    /// </summary>
    public const int PostCategoryWindowDays = 90;

    /// <summary>
    /// Minimum token length used by the Jaccard tokenizer (drops words shorter than this).
    /// </summary>
    public const int MinTokenLength = 3;
}
