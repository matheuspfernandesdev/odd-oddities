using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// PostgreSQL implementation of IPostAuditRepository using Entity Framework Core.
/// Provides data access for PostAudit operations.
/// </summary>
public sealed class PostgresPostAuditRepository : IPostAuditRepository
{
    private readonly OddOdditiesDbContext _context;

    public PostgresPostAuditRepository(OddOdditiesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<PostAudit> CreateAsync(
        PostAudit audit,
        CancellationToken cancellationToken = default)
    {
        if (audit is null)
            throw new ArgumentNullException(nameof(audit));

        _context.PostAudits.Add(audit);
        await _context.SaveChangesAsync(cancellationToken);
        return audit;
    }
}
