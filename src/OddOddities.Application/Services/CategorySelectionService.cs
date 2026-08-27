using Microsoft.Extensions.Logging;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Service for balanced category and subcategory selection.
/// Implements RF-06: Selects the least used category and subcategory
/// from the last 90 days to ensure content variety.
/// </summary>
public sealed class CategorySelectionService : ICategorySelectionPort
{
    private readonly IPostRepository _postRepository;
    private readonly ILogger<CategorySelectionService> _logger;

    public CategorySelectionService(
        IPostRepository postRepository,
        ILogger<CategorySelectionService> logger)
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