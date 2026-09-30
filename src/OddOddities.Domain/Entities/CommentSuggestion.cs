using OddOddities.Domain.Enums;

namespace OddOddities.Domain.Entities;

/// <summary>
/// Audit and idempotency record for a read Instagram comment (RF-19, section 3.4).
/// Every new comment is persisted exactly once; <see cref="CommentId"/> has a unique index.
/// </summary>
public sealed class CommentSuggestion
{
    public long Id { get; set; }

    /// <summary>Meta comment id — unique index, the idempotency key.</summary>
    public string CommentId { get; set; } = string.Empty;

    /// <summary>Instagram media id where the comment was posted.</summary>
    public string MediaId { get; set; } = string.Empty;

    /// <summary>Username of the comment author (used for the "Suggested by @user" credit).</summary>
    public string AuthorUsername { get; set; } = string.Empty;

    /// <summary>Original comment text.</summary>
    public string CommentText { get; set; } = string.Empty;

    public CommentClassification Classification { get; set; } = CommentClassification.NotSuggestion;

    /// <summary>Why the suggestion was rejected; null when not rejected.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>When the comment was processed (UTC).</summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
