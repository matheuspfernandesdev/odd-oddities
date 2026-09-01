using Microsoft.Extensions.Logging;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.UseCases;

/// <summary>
/// Use case for balanced category and subcategory selection (RF-06).
/// Selects the least used category and subcategory from the last
/// <see cref="OddOddities.Domain.Constants.PipelineConstants.DefaultCategoryRotationWindowDays"/> days
/// to ensure content variety.
/// </summary>
public sealed class SelectBalancedCategoryUseCase : ICategorySelectionPort
{
    private readonly IPostRepository _postRepository;
    private readonly ILogger<SelectBalancedCategoryUseCase> _logger;

    public SelectBalancedCategoryUseCase(
        IPostRepository postRepository,
        ILogger<SelectBalancedCategoryUseCase> logger)
    {
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(Category Category, Subcategory Subcategory)> SelectBalancedCategoryAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting balanced category selection (RF-06)");

        var (category, subcategory) = await _postRepository.GetLeastUsedCategoryAsync(cancellationToken);

        _logger.LogInformation(
            "Selected category '{CategoryName}' (ID: {CategoryId}) and subcategory '{SubcategoryName}' (ID: {SubcategoryId})",
            category.Name,
            category.Id,
            subcategory.Name,
            subcategory.Id);

        return (category, subcategory);
    }
}
