using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Builds the ordered model candidate chain used by the text/image pipeline steps:
/// preferred model from config first, then dynamic candidates from the OpenRouter
/// catalog (free first, then cheapest within the configured cost ceiling).
/// The catalog is fetched lazily once per DI scope (= once per pipeline execution).
/// On catalog failure it falls back to the preferred model only, so the pipeline
/// keeps working exactly as before.
/// </summary>
public interface IModelSelectionService
{
    Task<IReadOnlyList<ModelDescriptor>> GetTextChainAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelDescriptor>> GetImageChainAsync(CancellationToken cancellationToken = default);
}
