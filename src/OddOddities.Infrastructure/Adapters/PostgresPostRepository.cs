using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
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

        return await _context.Posts
            .AsNoTracking()
            .AnyAsync(p => p.ContentHash == contentHash && p.CreatedAt >= cutoffDate, cancellationToken);
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
        // This is a placeholder implementation. The actual similarity check
        // will be implemented in a separate service (RF-07).
        // For now, we return an empty list.
        return await Task.FromResult<IReadOnlyList<Post>>(new List<Post>());
    }
}