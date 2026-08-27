using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// PostgreSQL implementation of IPublicationRepository using Entity Framework Core.
/// Provides data access for Publication operations.
/// </summary>
public sealed class PostgresPublicationRepository : IPublicationRepository
{
    private readonly OddOdditiesDbContext _context;

    public PostgresPublicationRepository(OddOdditiesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<Publication?> GetByPostIdAsync(long postId, CancellationToken cancellationToken = default)
    {
        return await _context.Publications
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PostId == postId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Publication> CreateAsync(Publication publication, CancellationToken cancellationToken = default)
    {
        _context.Publications.Add(publication);
        await _context.SaveChangesAsync(cancellationToken);
        return publication;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Publication publication, CancellationToken cancellationToken = default)
    {
        _context.Publications.Update(publication);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
