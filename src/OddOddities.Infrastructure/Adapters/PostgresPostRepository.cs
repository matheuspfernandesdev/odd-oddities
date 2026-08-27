using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// PostgreSQL implementation of IPostRepository using Entity Framework Core.
/// Provides data access for Post operations including balanced category selection.
/// </summary>
public sealed class PostgresPostRepository : IPostRepository
{
    private readonly OddOdditiesDbContext _context;

    public PostgresPostRepository(OddOdditiesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<Post?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.Posts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Post> CreateAsync(Post post, CancellationToken cancellationToken = default)
    {
        _context.Posts.Add(post);
        await _context.SaveChangesAsync(cancellationToken);
        return post;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Post post, CancellationToken cancellationToken = default)
    {
        _context.Posts.Update(post);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Post>> GetRecentPostsAsync(
        int days = 90,
        CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);

        return await _context.Posts
            .AsNoTracking()
            .Where(p => p.CreatedAt >= cutoffDate)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByContentHashAsync(
        string contentHash,
        int days = 90,
        CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);

        // BR-004: ContentHash must not exist in a Published Post from the last 90 days
        return await _context.Posts
            .AsNoTracking()
            .AnyAsync(
                p => p.ContentHash == contentHash
                    && p.Status == PostStatus.Published
                    && p.PublishedAt >= cutoffDate,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(Category Category, Subcategory Subcategory)> GetLeastUsedCategoryAsync(
        CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-90);

        // RF-06: Select the least used category from the last 90 days
        // Uses LEFT JOIN with Posts where PublishedAt >= cutoff to count usage
        // Ties are broken alphabetically by category name
        var categoryUsage = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .GroupJoin(
                _context.Posts.Where(p => p.PublishedAt >= cutoffDate),
                c => c.Id,
                p => p.CategoryId,
                (category, posts) => new
                {
                    Category = category,
                    UsageCount = posts.Count()
                })
            .OrderBy(x => x.UsageCount)
            .ThenBy(x => x.Category.Name)
            .Select(x => new { x.Category.Id, x.Category.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (categoryUsage == null)
        {
            throw new InvalidOperationException("No active categories available for selection.");
        }

        // RF-06: Within the selected category, find the least used subcategory
        // Ties are broken alphabetically by subcategory name
        var subcategoryUsage = await _context.Subcategories
            .AsNoTracking()
            .Where(s => s.CategoryId == categoryUsage.Id && s.IsActive)
            .GroupJoin(
                _context.Posts.Where(p => p.PublishedAt >= cutoffDate),
                s => s.Id,
                p => p.SubcategoryId,
                (subcategory, posts) => new
                {
                    Subcategory = subcategory,
                    UsageCount = posts.Count()
                })
            .OrderBy(x => x.UsageCount)
            .ThenBy(x => x.Subcategory.Name)
            .Select(x => new { x.Subcategory.Id, x.Subcategory.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (subcategoryUsage == null)
        {
            throw new InvalidOperationException(
                $"No active subcategories available for category '{categoryUsage.Name}' (ID: {categoryUsage.Id}).");
        }

        // Fetch full entities for return
        var category = await _context.Categories
            .AsNoTracking()
            .FirstAsync(c => c.Id == categoryUsage.Id, cancellationToken);

        var subcategory = await _context.Subcategories
            .AsNoTracking()
            .FirstAsync(s => s.Id == subcategoryUsage.Id, cancellationToken);

        return (category, subcategory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Post>> SearchBySummarySimilarityAsync(
        string summary,
        double threshold = 0.80,
        int days = 90,
        CancellationToken cancellationToken = default)
    {
        // Jaccard similarity must be computed in memory — it cannot be expressed
        // efficiently in SQL. Fetch all published summaries from the last 90 days,
        // compute similarity, and return those that exceed the threshold.

        var cutoffDate = DateTime.UtcNow.AddDays(-days);

        var publishedSummaries = await _context.Posts
            .AsNoTracking()
            .Where(p => p.Status == PostStatus.Published && p.PublishedAt >= cutoffDate)
            .Select(p => new Post { Id = p.Id, Summary = p.Summary, ContentHash = p.ContentHash })
            .ToListAsync(cancellationToken);

        var similarPosts = new List<Post>();

        foreach (var post in publishedSummaries)
        {
            var similarity = JaccardSimilarity(summary, post.Summary);
            if (similarity >= threshold)
            {
                similarPosts.Add(post);
            }
        }

        return similarPosts.AsReadOnly();
    }

    /// <summary>
    /// Computes Jaccard similarity between two texts using normalized token sets.
    /// Used internally for database-level similarity queries (RF-07, BR-005).
    /// </summary>
    private static double JaccardSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        var tokens1 = NormalizeAndTokenize(text1);
        var tokens2 = NormalizeAndTokenize(text2);

        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 0.0;

        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// Tokenizes text into a set of normalized tokens for Jaccard similarity.
    /// </summary>
    private static HashSet<string> NormalizeAndTokenize(string text)
    {
        var punctuation = new[] { '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}' };
        var tokens = text
            .ToLowerInvariant()
            .Normalize()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(punctuation))
            .Where(t => t.Length >= 3);

        return new HashSet<string>(tokens);
    }
}