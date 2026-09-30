using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;
using OddOddities.Infrastructure.Data;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// PostgreSQL implementation of ICommentSuggestionRepository using Entity Framework Core.
/// The unique index on CommentId is the final idempotency guard: even a concurrent
/// insert of the same comment fails at the database level.
/// </summary>
public sealed class PostgresCommentSuggestionRepository : ICommentSuggestionRepository
{
    private readonly OddOdditiesDbContext _context;

    public PostgresCommentSuggestionRepository(OddOdditiesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCommentIdAsync(
        string commentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CommentSuggestions
            .AsNoTracking()
            .AnyAsync(cs => cs.CommentId == commentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommentSuggestion> CreateAsync(
        CommentSuggestion suggestion,
        CancellationToken cancellationToken = default)
    {
        _context.CommentSuggestions.Add(suggestion);
        await _context.SaveChangesAsync(cancellationToken);
        return suggestion;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        CommentSuggestion suggestion,
        CancellationToken cancellationToken = default)
    {
        // Guard against EF Core identity conflict, mirroring PostgresPostRepository:
        // a detached instance may collide with another tracked instance of the same key
        // (e.g. created earlier in the same scoped DbContext).
        var tracked = _context.CommentSuggestions.Local.FirstOrDefault(cs => cs.Id == suggestion.Id);
        if (tracked is not null && !ReferenceEquals(tracked, suggestion))
        {
            _context.Entry(tracked).State = EntityState.Detached;
        }

        _context.CommentSuggestions.Update(suggestion);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
