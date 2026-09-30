namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for reading and replying to Instagram media comments via the Meta Graph API (RF-19).
/// </summary>
public interface IMediaCommentPort
{
    /// <summary>
    /// Gets every top-level comment of the given media, following pagination until the
    /// comment list is exhausted. Comments arrive in reverse-chronological order.
    /// </summary>
    Task<IReadOnlyList<MediaComment>> GetCommentsAsync(
        string mediaId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a reply to an existing comment (POST /{comment-id}/replies).
    /// </summary>
    Task ReplyToCommentAsync(
        string commentId,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A single Instagram media comment as returned by the Graph API (RF-19).
/// </summary>
/// <param name="CommentId">Meta comment id — unique per comment, used as the idempotency key.</param>
/// <param name="Text">Original comment text.</param>
/// <param name="Timestamp">When the comment was posted (UTC).</param>
/// <param name="AuthorUsername">Username of the comment author (from <c>from.username</c>).</param>
public sealed record MediaComment(
    string CommentId,
    string Text,
    DateTime Timestamp,
    string AuthorUsername);
