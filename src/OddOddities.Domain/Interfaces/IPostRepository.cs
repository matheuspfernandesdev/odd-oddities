using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for Post persistence operations.
/// </summary>
public interface IPostRepository
{
    Task<Post?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<Post> CreateAsync(Post post, CancellationToken cancellationToken = default);

    Task UpdateAsync(Post post, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Post>> GetRecentPostsAsync(
        int days = 90,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByContentHashAsync(
        string contentHash,
        int days = 90,
        CancellationToken cancellationToken = default);

    Task<(Category Category, Subcategory Subcategory)> GetLeastUsedCategoryAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Post>> SearchBySummarySimilarityAsync(
        string summary,
        double threshold = 0.80,
        int days = 90,
        CancellationToken cancellationToken = default);
}
