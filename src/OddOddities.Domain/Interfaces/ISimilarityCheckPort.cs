namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for textual similarity checking (RF-07).
/// Computes ContentHash and Jaccard similarity to detect duplicate or similar content.
/// Business rules: BR-004 (ContentHash), BR-005 (similarity), BR-006 (max 3 attempts).
/// </summary>
public interface ISimilarityCheckPort
{
    /// <summary>
    /// Computes the SHA-256 ContentHash of the given text content.
    /// Normalizes: lowercase, removes punctuation, collapses whitespace.
    /// </summary>
    /// <param name="textContent">The raw text content to hash.</param>
    /// <returns>The lowercase hex SHA-256 hash.</returns>
    string ComputeContentHash(string textContent);

    /// <summary>
    /// Calculates the Jaccard similarity between two texts using normalized token sets.
    /// Returns a value between 0.0 (no overlap) and 1.0 (identical).
    /// </summary>
    /// <param name="text1">First text.</param>
    /// <param name="text2">Second text.</param>
    /// <returns>Jaccard similarity coefficient.</returns>
    double CalculateJaccardSimilarity(string text1, string text2);

    /// <summary>
    /// Checks if the ContentHash already exists in a published Post from the last 90 days (BR-004).
    /// </summary>
    /// <param name="contentHash">The computed hash to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a duplicate exists; false otherwise.</returns>
    Task<bool> IsContentHashDuplicateAsync(
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the Summary text is too similar (>= threshold) to any published Post from the last 90 days (BR-005).
    /// </summary>
    /// <param name="summary">The new summary text.</param>
    /// <param name="threshold">Similarity threshold (default 0.80).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a similar summary exists; false otherwise.</returns>
    Task<bool> IsSummarySimilarAsync(
        string summary,
        double threshold = 0.80,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a full duplicate check: ContentHash + Summary similarity.
    /// Returns a detailed result indicating which check failed, if any.
    /// </summary>
    /// <param name="contentHash">Computed ContentHash.</param>
    /// <param name="summary">New summary text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Check result with details.</returns>
    Task<SimilarityCheckResult> CheckAsync(
        string contentHash,
        string summary,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a similarity check operation.
/// </summary>
public sealed class SimilarityCheckResult
{
    public bool IsDuplicate { get; }
    public bool IsHashDuplicate { get; }
    public bool IsSimilar { get; }
    public double MaxSimilarityScore { get; }

    private SimilarityCheckResult(
        bool isDuplicate,
        bool isHashDuplicate,
        bool isSimilar,
        double maxSimilarityScore)
    {
        IsDuplicate = isDuplicate;
        IsHashDuplicate = isHashDuplicate;
        IsSimilar = isSimilar;
        MaxSimilarityScore = maxSimilarityScore;
    }

    public static SimilarityCheckResult Clean() => new(false, false, false, 0.0);

    public static SimilarityCheckResult HashDuplicate() => new(true, true, false, 0.0);

    public static SimilarityCheckResult Similar(double score) => new(true, false, true, score);

    public static SimilarityCheckResult Both(double similarityScore) => new(true, true, true, similarityScore);
}
