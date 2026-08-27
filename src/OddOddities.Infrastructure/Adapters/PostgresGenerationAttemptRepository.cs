using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// PostgreSQL implementation of IGenerationAttemptRepository using Entity Framework Core.
/// Provides data access for GenerationAttempt operations.
/// </summary>
public sealed class PostgresGenerationAttemptRepository : IGenerationAttemptRepository
{
    private readonly OddOdditiesDbContext _context;

    public PostgresGenerationAttemptRepository(OddOdditiesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<GenerationAttempt> CreateAsync(
        GenerationAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (attempt is null)
            throw new ArgumentNullException(nameof(attempt));

        _context.GenerationAttempts.Add(attempt);
        await _context.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    /// <inheritdoc />
    public async Task<int> GetAttemptCountForPostAsync(
        long postId,
        CancellationToken cancellationToken = default)
    {
        return await _context.GenerationAttempts
            .AsNoTracking()
            .CountAsync(a => a.PostId == postId, cancellationToken);
    }
}
