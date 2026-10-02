namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for image generation via OpenRouter.
/// The model is resolved by the caller (preferred model + dynamic fallback chain).
/// </summary>
public interface IImageGenerationPort
{
    Task<ImageGenerationResult> GenerateImageAsync(
        string prompt,
        string modelId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a successful image generation call, including usage/cost telemetry.
/// </summary>
public sealed record ImageGenerationResult(
    byte[] ImageBytes,
    string ModelId,
    decimal? CostUsd,
    long DurationMs);
