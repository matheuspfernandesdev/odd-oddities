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
    private bool _videoCatalogLoaded;
    private IReadOnlyList<ModelDescriptor>? _textCatalog;
    private IReadOnlyList<ModelDescriptor>? _imageCatalog;
    private IReadOnlyList<VideoModelDescriptor>? _videoCatalog;

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
            idSelector: m => m.Id,
            isEligible: m => IsTextEligible(m, settings),
            createPreferredFallback: id => new ModelDescriptor(
                Id: id,
                Name: id,
                PromptPricePerToken: null,
                CompletionPricePerToken: null,
                ImageOutputPrice: null,
                IsFree: false,
                ContextLength: null,
                Created: 0));
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
            idSelector: m => m.Id,
            isEligible: m => IsImageEligible(m, settings),
            createPreferredFallback: id => new ModelDescriptor(
                Id: id,
                Name: id,
                PromptPricePerToken: null,
                CompletionPricePerToken: null,
                ImageOutputPrice: null,
                IsFree: false,
                ContextLength: null,
                Created: 0));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoModelDescriptor>> GetVideoChainAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await LoadVideoCatalogAsync(cancellationToken);
        var settings = _config.ModelSelection;
        var video = _config.Video;

        // Guarantee free -> cheapest-per-second order regardless of catalog order.
        var orderedCatalog = catalog?
            .OrderByDescending(m => m.IsFree)
            .ThenBy(m => m.PricePerSecondUsd ?? decimal.MaxValue)
            .ToList();

        return BuildChain(
            preferredId: video.ModelId,
            catalog: orderedCatalog,
            maxModels: settings.MaxVideoModelAttempts,
            idSelector: m => m.Id,
            isEligible: m => IsVideoEligible(m, video, settings),
            createPreferredFallback: id => new VideoModelDescriptor(
                Id: id,
                Name: id,
                PricePerSecondUsd: null,
                SupportedDurations: Array.Empty<int>(),
                SupportedAspectRatios: Array.Empty<string>(),
                Created: 0));
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

    private async Task<IReadOnlyList<VideoModelDescriptor>?> LoadVideoCatalogAsync(CancellationToken cancellationToken)
    {
        if (_videoCatalogLoaded)
            return _videoCatalog;

        _videoCatalogLoaded = true;

        try
        {
            _videoCatalog = await _modelCatalog.GetVideoModelsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch OpenRouter video model catalog; falling back to configured model only");
            _videoCatalog = null;
        }

        return _videoCatalog;
    }

    private IReadOnlyList<T> BuildChain<T>(
        string preferredId,
        IReadOnlyList<T>? catalog,
        int maxModels,
        Func<T, string> idSelector,
        Func<T, bool> isEligible,
        Func<string, T> createPreferredFallback)
        where T : class
    {
        var chain = new List<T>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            // Preferred model goes first even when missing from the catalog
            // (unknown pricing is tolerated for the configured model only).
            var preferred = catalog?.FirstOrDefault(m =>
                                string.Equals(idSelector(m), preferredId, StringComparison.OrdinalIgnoreCase))
                            ?? createPreferredFallback(preferredId);

            chain.Add(preferred);
            seen.Add(idSelector(preferred));
        }

        if (catalog is not null)
        {
            foreach (var model in catalog)
            {
                if (chain.Count >= maxModels)
                    break;

                if (!seen.Add(idSelector(model)))
                    continue;

                if (!isEligible(model))
                {
                    seen.Remove(idSelector(model));
                    continue;
                }

                chain.Add(model);
            }
        }

        _logger.LogInformation(
            "Built model chain ({Count} models, max {Max}): {Models}",
            chain.Count,
            maxModels,
            string.Join(" -> ", chain.Select(idSelector)));

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

    private static bool IsVideoEligible(
        VideoModelDescriptor model,
        VideoConfiguration video,
        ModelSelectionConfiguration settings)
    {
        if (!model.SupportedAspectRatios.Contains(video.AspectRatio, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!model.SupportedDurations.Contains(video.DurationSeconds))
            return false;

        // Skip models with unknown pricing (cannot verify the per-second cap); only the
        // configured model may reach the chain without a known price.
        if (model.PricePerSecondUsd is null)
            return false;

        return model.PricePerSecondUsd * video.DurationSeconds <= settings.MaxVideoCostPerRequestUsd;
    }
}
