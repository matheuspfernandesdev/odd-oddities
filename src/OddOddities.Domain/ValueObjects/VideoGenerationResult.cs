namespace OddOddities.Domain.ValueObjects;

/// <summary>
/// Result of a successful asynchronous video generation call (RF-17).
/// VideoBytes holds the raw MP4 payload. DurationSeconds is the clip duration that was
/// requested from the provider (the effective, clamped value - see
/// <see cref="VideoConfiguration.GetEffectiveDurationSeconds"/>); it flows to
/// Post.VideoDurationSeconds and PipelineContext.Video. CostUsd is the real usage cost
/// reported by OpenRouter (null when the API did not report one, so callers can fall
/// back to their pre-call estimate).
/// </summary>
public sealed record VideoGenerationResult(
    byte[] VideoBytes,
    string ModelId,
    decimal? CostUsd,
    int DurationSeconds);
