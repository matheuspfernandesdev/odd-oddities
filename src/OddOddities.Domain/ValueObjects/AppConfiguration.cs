using OddOddities.Domain.Constants;

namespace OddOddities.Domain.ValueObjects;

/// <summary>
/// Root configuration class that aggregates all application settings.
/// Uses IOptions pattern for strongly-typed configuration.
/// </summary>
public sealed class AppConfiguration
{
    public const string SectionName = "AppConfiguration";

    /// <summary>
    /// Maximum length for caption text content (BR-002). Default: 800 characters.
    /// </summary>
    public int MaxCaptionContentLength { get; set; } = 800;

    public ConnectionStringsConfiguration ConnectionStrings { get; set; } = new();
    public OpenRouterConfiguration OpenRouter { get; set; } = new();
    public ModelSelectionConfiguration ModelSelection { get; set; } = new();
    public MetaConfiguration Meta { get; set; } = new();
    public MinioConfiguration MinIO { get; set; } = new();
    public TokenEncryptionConfiguration TokenEncryption { get; set; } = new();
    public ScheduleConfiguration Schedule { get; set; } = new();
    public ImageProcessingConfiguration ImageProcessing { get; set; } = new();
    public VideoConfiguration Video { get; set; } = new();
}

/// <summary>
/// Database connection strings.
/// </summary>
public sealed class ConnectionStringsConfiguration
{
    public string DefaultConnection { get; set; } = string.Empty;
}

/// <summary>
/// OpenRouter API configuration for text and image generation.
/// </summary>
public sealed class OpenRouterConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string TextModelId { get; set; } = string.Empty;
    public string ImageModelId { get; set; } = string.Empty;
}

/// <summary>
/// Model selection / fallback configuration. Controls how many distinct models are
/// tried per step and the cost ceilings applied when building the candidate chain
/// from the OpenRouter model catalog. All values have code defaults, so the section
/// is optional in appsettings.
/// </summary>
public sealed class ModelSelectionConfiguration
{
    /// <summary>Max distinct text models tried per pipeline run (fallback cap).</summary>
    public int MaxTextModelAttempts { get; set; } = 5;

    /// <summary>Max distinct image models tried per pipeline run (fallback cap).</summary>
    public int MaxImageModelAttempts { get; set; } = 3;

    /// <summary>Max total cost (USD) for a single pipeline execution (text + image).</summary>
    public decimal MaxCostPerRunUsd { get; set; } = 0.05m;

    /// <summary>Max total cost (USD) for a single video pipeline execution (text + video).</summary>
    public decimal MaxCostPerVideoRunUsd { get; set; } = 0.20m;

    /// <summary>Max estimated cost (USD) per text request for a candidate to enter the chain.</summary>
    public decimal MaxTextCostPerRequestUsd { get; set; } = 0.01m;

    /// <summary>Max estimated cost (USD) per image request for a candidate to enter the chain.</summary>
    public decimal MaxImageCostPerRequestUsd { get; set; } = 0.05m;

    /// <summary>Max estimated cost (USD) per video request (cost/second × duration) for a candidate to enter the chain.</summary>
    public decimal MaxVideoCostPerRequestUsd { get; set; } = 0.15m;

    /// <summary>Max distinct video models tried per pipeline run (fallback cap).</summary>
    public int MaxVideoModelAttempts { get; set; } = 3;

    /// <summary>Minimum context length for a text model to be considered a candidate.</summary>
    public int MinContextLength { get; set; } = 8000;
}

/// <summary>
/// Meta (Instagram) Graph API configuration.
/// </summary>
public sealed class MetaConfiguration
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string InstagramUserId { get; set; } = string.Empty;
}

/// <summary>
/// MinIO object storage configuration.
/// </summary>
public sealed class MinioConfiguration
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "odd-oddities";
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Maximum bucket size in bytes (BR-009). Default: 20 GB (21,474,836,480 bytes).
    /// When this quota is reached, uploads are blocked.
    /// </summary>
    public long QuotaBytes { get; set; } = 21_474_836_480L;
}

/// <summary>
/// Token encryption configuration (AES-256-GCM).
/// </summary>
public sealed class TokenEncryptionConfiguration
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Scheduler configuration for pipeline execution.
/// </summary>
public sealed class ScheduleConfiguration
{
    public int HourUtc { get; set; } = 17;
    public string Timezone { get; set; } = "Eastern Standard Time";
    public string Days { get; set; } = "TUE,THU,SAT";
}

/// <summary>
/// Image processing configuration.
/// </summary>
public sealed class ImageProcessingConfiguration
{
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1080;
    public int Quality { get; set; } = 85;
    public string WatermarkText { get; set; } = "Odd Oddities";
    public int WatermarkFontSize { get; set; } = 24;
}

/// <summary>
/// Video generation configuration (RF-15). IntervalDays drives the "one video every
/// N days" cadence decided by ISchedulerPort.IsVideoRunToday(); the remaining values
/// describe the target video shape. All values have code defaults, so the section is
/// optional in appsettings.
/// </summary>
public sealed class VideoConfiguration
{
    /// <summary>Preferred video model id (first in the fallback chain).</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Video duration in seconds. Default: 5 (Meta Reels accepts 3-8s).</summary>
    public int DurationSeconds { get; set; } = 5;

    /// <summary>Video resolution, e.g. "480p".</summary>
    public string Resolution { get; set; } = "480p";

    /// <summary>Aspect ratio, e.g. "9:16" for vertical Reels.</summary>
    public string AspectRatio { get; set; } = "9:16";

    /// <summary>Whether the video model should also generate audio.</summary>
    public bool GenerateAudio { get; set; } = true;

    /// <summary>Minimum days between two published videos. Default: 15.</summary>
    public int IntervalDays { get; set; } = 15;

    /// <summary>
    /// Effective clip duration sent to the video API (RF-17): the configured value clamped
    /// to the Reels-compatible range [MinVideoDurationSeconds, MaxVideoDurationSeconds].
    /// Single source of truth for the request parameter, the pre-call budget estimate and
    /// Post.VideoDurationSeconds.
    /// </summary>
    public int GetEffectiveDurationSeconds()
        => Math.Clamp(
            DurationSeconds,
            PipelineConstants.MinVideoDurationSeconds,
            PipelineConstants.MaxVideoDurationSeconds);
}
