namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for classifying Instagram comments with an LLM in a single call (RF-20).
/// The model id is resolved by the caller (first candidate of the existing text
/// fallback chain — GetTextChainAsync), so classification rides the free models.
/// </summary>
public interface ICommentClassificationPort
{
    /// <summary>
    /// Classifies the given new comments in one batch: is the comment a suggestion of a
    /// factual theme adequate for this profile? When it is, the model extracts a
    /// normalized theme and a short summary.
    /// </summary>
    /// <param name="comments">New comments to classify (never empty).</param>
    /// <param name="modelId">Model to use (caller-resolved fallback chain).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One <see cref="CommentClassificationResult"/> per requested comment id. Ids the
    /// model did not answer for are simply missing — the caller falls back to
    /// NotSuggestion so every comment is still persisted (RF-20 AC4).
    /// </returns>
    Task<IReadOnlyList<CommentClassificationResult>> ClassifyCommentsAsync(
        IReadOnlyList<MediaComment> comments,
        string modelId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Classification of a single Instagram comment produced by the AI call (RF-20).
/// </summary>
/// <param name="CommentId">Meta comment id this classification refers to.</param>
/// <param name="IsSuggestion">Whether the comment suggests an adequate factual theme.</param>
/// <param name="Theme">Normalized theme label when <paramref name="IsSuggestion"/> is true.</param>
/// <param name="Summary">Short summary of the suggested theme when <paramref name="IsSuggestion"/> is true.</param>
public sealed record CommentClassificationResult(
    string CommentId,
    bool IsSuggestion,
    string Theme,
    string Summary);
