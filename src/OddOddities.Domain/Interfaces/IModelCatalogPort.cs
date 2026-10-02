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
