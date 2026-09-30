namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for querying the OpenRouter model catalog (list of available models and pricing).
/// Used to build dynamic model fallback chains at the start of each pipeline execution.
/// </summary>
public interface IModelCatalogPort
{
    /// <summary>
    /// Returns text-output models ordered cheapest first (free models included).
    /// </summary>
    Task<IReadOnlyList<ModelDescriptor>> GetTextModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns image-generation models ordered cheapest first (by image output price).
    /// </summary>
    Task<IReadOnlyList<ModelDescriptor>> GetImageModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns video-generation models ordered cheapest first (free models first, then
    /// by effective cost per second of video). Models without a per-second-of-video SKU
    /// (the documented "per-video-second*" form or the live "duration_seconds*" form) are
    /// excluded (not eligible for selection).
    /// </summary>
    Task<IReadOnlyList<VideoModelDescriptor>> GetVideoModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Normalized descriptor of a model available on OpenRouter.
/// Pricing values are USD (prompt/completion per token; image output per image).
/// A null price means unknown or dynamic pricing (e.g. router sentinel "-1").
/// OutputModalities contains values like "text" and/or "image" (from architecture.output_modalities).
/// </summary>
public sealed record ModelDescriptor(
    string Id,
    string Name,
    decimal? PromptPricePerToken,
    decimal? CompletionPricePerToken,
    decimal? ImageOutputPrice,
    bool IsFree,
    int? ContextLength,
    long Created,
    IReadOnlyList<string>? OutputModalities = null)
{
    public bool OutputsText =>
        OutputModalities is null || OutputModalities.Count == 0 || OutputModalities.Contains("text");

    public bool OutputsImage => OutputModalities?.Contains("image") == true;
}

/// <summary>
/// Normalized descriptor of a video-generation model available on OpenRouter.
/// PricePerSecondUsd is the effective cost per second of video (USD), normalized from
/// the lowest per-second SKU of the model. SKU keys are provider-dependent: the OpenRouter
/// docs show "per-video-second" (optionally variant-suffixed, e.g. "per-video-second-1080p")
/// while the live /videos/models catalog uses "duration_seconds*" forms (e.g.
/// "duration_seconds_480p", "text_to_video_duration_seconds_720p"). A null price means
/// unknown pricing (only possible for the configured model when it is missing from the
/// catalog).
/// SupportedDurations is in seconds; SupportedAspectRatios values look like "9:16".
/// </summary>
public sealed record VideoModelDescriptor(
    string Id,
    string Name,
    decimal? PricePerSecondUsd,
    IReadOnlyList<int> SupportedDurations,
    IReadOnlyList<string> SupportedAspectRatios,
    long Created)
{
    /// <summary>True when the model costs nothing per second of video.</summary>
    public bool IsFree => PricePerSecondUsd == 0m;
}
