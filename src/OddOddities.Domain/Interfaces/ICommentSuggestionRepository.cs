using OddOddities.Domain.Entities;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for CommentSuggestion persistence (RF-19). CommentId carries a unique index,
/// so a comment is never processed twice.
/// </summary>
public interface ICommentSuggestionRepository
{
    /// <summary>
    /// Returns true when a comment with the given Meta CommentId was already processed.
    /// </summary>
    Task<bool> ExistsByCommentIdAsync(
        string commentId,
        CancellationToken cancellationToken = default);

    Task<CommentSuggestion> CreateAsync(
        CommentSuggestion suggestion,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        CommentSuggestion suggestion,
        CancellationToken cancellationToken = default);
}
