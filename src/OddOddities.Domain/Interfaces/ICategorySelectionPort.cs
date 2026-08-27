using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for balanced category and subcategory selection.
/// Implements RF-06: Selects the least used category and subcategory
/// from the last 90 days to ensure content variety.
/// </summary>
public interface ICategorySelectionPort
{
    /// <summary>
    /// Selects a balanced category and subcategory based on usage in the last 90 days.
    /// Returns the least used category, and within it, the least used subcategory.
    /// Ties are broken alphabetically.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the selected Category and Subcategory.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no active categories or subcategories are available.
    /// </exception>
    Task<(Category Category, Subcategory Subcategory)> SelectBalancedCategoryAsync(
        CancellationToken cancellationToken = default);
}