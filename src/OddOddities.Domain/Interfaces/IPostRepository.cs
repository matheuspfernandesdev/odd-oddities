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

    /// <summary>
    /// Gets the PublishedAt timestamp of the most recent published Post that carries a
    /// video (VideoObjectKey != null), or null when no video was ever published (RF-15).
    /// </summary>
    Task<DateTime?> GetLatestVideoPublishedAtAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Meta media ids of the most recent published Posts that carry a
    /// Publication.MetaMediaId, newest first, limited to <paramref name="limit"/> (RF-19).
    /// Used as the comment lookback window (Comments.LookbackPosts).
    /// </summary>
    Task<IReadOnlyList<string>> GetLatestPublishedMediaIdsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Post>> SearchBySummarySimilarityAsync(
        string summary,
        double threshold = 0.80,
        int days = 90,
        CancellationToken cancellationToken = default);
}
