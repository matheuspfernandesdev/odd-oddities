using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// Service for textual similarity checking (RF-07).
/// Implements ISimilarityCheckPort with ContentHash (SHA-256) and Jaccard similarity algorithms.
/// Business rules: BR-004 (ContentHash duplicate), BR-005 (similarity >= 80%), BR-006 (max 3 attempts).
/// </summary>
public sealed class SimilarityCheckService : ISimilarityCheckPort
{
    private readonly IPostRepository _postRepository;
    private readonly ILogger<SimilarityCheckService> _logger;

    private static readonly Regex PunctuationRegex = new(@"[.,!?;:'""()\[\]{}\-_\\/]", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    public SimilarityCheckService(
        IPostRepository postRepository,
        ILogger<SimilarityCheckService> logger)
    {
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ComputeContentHash(string textContent)
    {
        if (string.IsNullOrWhiteSpace(textContent))
            return string.Empty;

        var normalized = textContent
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormC);

        normalized = PunctuationRegex.Replace(normalized, string.Empty);
        normalized = MultiSpaceRegex.Replace(normalized, " ").Trim();

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public double CalculateJaccardSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        var tokens1 = Tokenize(text1);
        var tokens2 = Tokenize(text2);

        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 0.0;

        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <inheritdoc />
    public async Task<bool> IsContentHashDuplicateAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return false;

        _logger.LogDebug("Checking ContentHash duplicate: {ContentHash}", contentHash);

        var exists = await _postRepository.ExistsByContentHashAsync(contentHash, PipelineConstants.DuplicateDetectionWindowDays, cancellationToken);

        if (exists)
        {
            _logger.LogWarning(
                "ContentHash duplicate detected: {ContentHash} exists in a published Post from the last {Days} days",
                contentHash,
                PipelineConstants.DuplicateDetectionWindowDays);
        }

        return exists;
    }

    /// <inheritdoc />
    public async Task<bool> IsSummarySimilarAsync(
        string summary,
        double threshold = 0.80,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return false;

        _logger.LogDebug(
            "Checking Summary similarity (threshold={Threshold:F2}): {Summary}",
            threshold,
            summary);

        var similarPosts = await _postRepository.SearchBySummarySimilarityAsync(
            summary,
            threshold,
            PipelineConstants.SimilaritySearchWindowDays,
            cancellationToken);

        if (similarPosts.Count > 0)
        {
            var maxScore = similarPosts
                .Select(p => CalculateJaccardSimilarity(summary, p.Summary))
                .Max();

            _logger.LogWarning(
                "Summary similarity detected: max score {Score:F4} (threshold {Threshold:F2})",
                maxScore,
                threshold);

            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<SimilarityCheckResult> CheckAsync(
        string contentHash,
        string summary,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting full similarity check: ContentHash={ContentHash}, Summary length={SummaryLength}",
            contentHash,
            summary?.Length ?? 0);

        var isHashDuplicate = await IsContentHashDuplicateAsync(contentHash, cancellationToken);

        var isSimilar = string.IsNullOrWhiteSpace(summary)
            ? false
            : await IsSummarySimilarAsync(summary, PipelineConstants.DefaultSimilarityThreshold, cancellationToken);

        var result = (isHashDuplicate, isSimilar) switch
        {
            (true, true) => SimilarityCheckResult.Both(0.0),
            (true, false) => SimilarityCheckResult.HashDuplicate(),
            (false, true) => SimilarityCheckResult.Similar(0.0),
            (false, false) => SimilarityCheckResult.Clean()
        };

        _logger.LogInformation(
            "Similarity check completed: IsDuplicate={IsDuplicate}, IsHashDuplicate={IsHashDuplicate}, IsSimilar={IsSimilar}",
            result.IsDuplicate,
            result.IsHashDuplicate,
            result.IsSimilar);

        return result;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = text
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}'))
            .Where(t => t.Length >= SimilarityConstants.MinTokenLength);

        return new HashSet<string>(tokens);
    }
}
