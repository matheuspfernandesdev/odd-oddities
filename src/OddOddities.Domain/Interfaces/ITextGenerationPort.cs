namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for text generation via OpenRouter.
/// The model is resolved by the caller (preferred model + dynamic fallback chain).
/// </summary>
public interface ITextGenerationPort
{
    Task<TextGenerationResult> GenerateCuriosityAsync(
        string category,
        string subcategory,
        string modelId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a successful text generation call, including usage/cost telemetry.
/// </summary>
public sealed record TextGenerationResult(
    string TextContent,
    string Summary,
    string Theme,
    string ImageFocus,
    string SourceUrl,
    string Category,
    string Subcategory,
    string ModelId,
    decimal? CostUsd,
    int? TokensIn,
    int? TokensOut,
    long DurationMs);
