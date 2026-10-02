using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Services;

/// <inheritdoc />
public sealed class ModelSelectionService : IModelSelectionService
{
    private readonly IModelCatalogPort _modelCatalog;
    private readonly AppConfiguration _config;
    private readonly ILogger<ModelSelectionService> _logger;

    private bool _textCatalogLoaded;
    private bool _imageCatalogLoaded;
    private IReadOnlyList<ModelDescriptor>? _textCatalog;
    private IReadOnlyList<ModelDescriptor>? _imageCatalog;

    public ModelSelectionService(
        IModelCatalogPort modelCatalog,
        IOptions<AppConfiguration> options,
        ILogger<ModelSelectionService> logger)
    {
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelDescriptor>> GetTextChainAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await LoadTextCatalogAsync(cancellationToken);
        var settings = _config.ModelSelection;

        return BuildChain(
            preferredId: _config.OpenRouter.TextModelId,
            catalog: catalog,
            maxModels: settings.MaxTextModelAttempts,
            isEligible: m => IsTextEligible(m, settings));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelDescriptor>> GetImageChainAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await LoadImageCatalogAsync(cancellationToken);
        var settings = _config.ModelSelection;

        return BuildChain(
            preferredId: _config.OpenRouter.ImageModelId,
            catalog: catalog,
            maxModels: settings.MaxImageModelAttempts,
            isEligible: m => IsImageEligible(m, settings));
    }

    private async Task<IReadOnlyList<ModelDescriptor>?> LoadTextCatalogAsync(CancellationToken cancellationToken)
    {
        if (_textCatalogLoaded)
            return _textCatalog;

        _textCatalogLoaded = true;

        try
        {
            _textCatalog = await _modelCatalog.GetTextModelsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch OpenRouter text model catalog; falling back to configured model only");
            _textCatalog = null;
        }

        return _textCatalog;
    }

    private async Task<IReadOnlyList<ModelDescriptor>?> LoadImageCatalogAsync(CancellationToken cancellationToken)
    {
        if (_imageCatalogLoaded)
            return _imageCatalog;

        _imageCatalogLoaded = true;

        try
        {
            _imageCatalog = await _modelCatalog.GetImageModelsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch OpenRouter image model catalog; falling back to configured model only");
            _imageCatalog = null;
        }

        return _imageCatalog;
    }

    private IReadOnlyList<ModelDescriptor> BuildChain(
        string preferredId,
        IReadOnlyList<ModelDescriptor>? catalog,
        int maxModels,
        Func<ModelDescriptor, bool> isEligible)
    {
        var chain = new List<ModelDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            // Preferred model goes first even when missing from the catalog
            // (unknown pricing is tolerated for the configured model only).
            var preferred = catalog?.FirstOrDefault(m =>
                                string.Equals(m.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                            ?? new ModelDescriptor(
                                Id: preferredId,
                                Name: preferredId,
                                PromptPricePerToken: null,
                                CompletionPricePerToken: null,
                                ImageOutputPrice: null,
                                IsFree: false,
                                ContextLength: null,
                                Created: 0);

            chain.Add(preferred);
            seen.Add(preferred.Id);
        }

        if (catalog is not null)
        {
            foreach (var model in catalog)
            {
                if (chain.Count >= maxModels)
                    break;

                if (!seen.Add(model.Id))
                    continue;

                if (!isEligible(model))
                {
                    seen.Remove(model.Id);
                    continue;
                }

                chain.Add(model);
            }
        }

        _logger.LogInformation(
            "Built model chain ({Count} models, max {Max}): {Models}",
            chain.Count,
            maxModels,
            string.Join(" -> ", chain.Select(m => m.Id)));

        return chain;
    }

    private static bool IsTextEligible(ModelDescriptor model, ModelSelectionConfiguration settings)
    {
        if (model.ContextLength is null || model.ContextLength < settings.MinContextLength)
            return false;

        // Skip models with unknown pricing (cannot verify the per-request cap).
        if (model.PromptPricePerToken is null && model.CompletionPricePerToken is null)
            return false;

        return ModelCostEstimator.EstimateTextCostPerRequest(model) <= settings.MaxTextCostPerRequestUsd;
    }

    private static bool IsImageEligible(ModelDescriptor model, ModelSelectionConfiguration settings)
    {
        // Image chain uses paid models only: free models have uncertain quality/stability
        // and would occupy the cheapest slots ahead of known paid options.
        if (model.IsFree)
            return false;

        if (model.ImageOutputPrice is null)
            return false;

        return model.ImageOutputPrice <= settings.MaxImageCostPerRequestUsd;
    }
}
