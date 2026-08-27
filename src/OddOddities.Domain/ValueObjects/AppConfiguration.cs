namespace OddOddities.Domain.ValueObjects;

/// <summary>
/// Root configuration class that aggregates all application settings.
/// Uses IOptions pattern for strongly-typed configuration.
/// </summary>
public sealed class AppConfiguration
{
    public const string SectionName = "AppConfiguration";

    public ConnectionStringsConfiguration ConnectionStrings { get; set; } = new();
    public OpenRouterConfiguration OpenRouter { get; set; } = new();
    public MetaConfiguration Meta { get; set; } = new();
    public MinioConfiguration MinIO { get; set; } = new();
    public TokenEncryptionConfiguration TokenEncryption { get; set; } = new();
    public ScheduleConfiguration Schedule { get; set; } = new();
    public ImageProcessingConfiguration ImageProcessing { get; set; } = new();
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
