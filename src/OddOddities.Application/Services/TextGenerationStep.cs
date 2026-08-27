using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Services;

/// <summary>
/// Pipeline step for text generation via OpenRouter (RF-01).
/// Generates curiosity content, validates length, computes ContentHash,
/// checks for duplicates and similarity, then creates the Post entity.
/// Business rules: BR-001 (factual content), BR-002 (max 800 chars),
/// BR-004 (ContentHash duplicate), BR-005 (similarity threshold).
/// </summary>
public sealed class TextGenerationStep : IPipelineStep
{
    private const int MaxGenerationAttempts = 3;

    private readonly ITextGenerationPort _textGenerationPort;
    private readonly ISimilarityCheckPort _similarityCheck;
    private readonly IPostRepository _postRepository;
    private readonly IOptions<AppConfiguration> _config;
    private readonly ILogger<TextGenerationStep> _logger;

    public string StepName => "TextGeneration";

    public TextGenerationStep(
        ITextGenerationPort textGenerationPort,
        ISimilarityCheckPort similarityCheck,
        IPostRepository postRepository,
        IOptions<AppConfiguration> config,
        ILogger<TextGenerationStep> logger)
    {
        _textGenerationPort = textGenerationPort ?? throw new ArgumentNullException(nameof(textGenerationPort));
        _similarityCheck = similarityCheck ?? throw new ArgumentNullException(nameof(similarityCheck));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(
        PipelineExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting text generation for {Category}/{Subcategory}",
            context.CategoryName,
            context.SubcategoryName);

        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            _logger.LogDebug(
                "Text generation attempt {Attempt}/{MaxAttempts}",
                attempt,
                MaxGenerationAttempts);

            // 1. Call OpenRouter to generate curiosity
            var curiosity = await _textGenerationPort.GenerateCuriosityAsync(
                context.CategoryName,
                context.SubcategoryName,
                cancellationToken);

            // 2. Validate text length (BR-002)
            if (curiosity.TextContent.Length > _config.Value.MaxCaptionContentLength)
            {
                _logger.LogWarning(
                    "TextContent exceeds max length: {Length} > {MaxLength}",
                    curiosity.TextContent.Length,
                    _config.Value.MaxCaptionContentLength);

                if (attempt == MaxGenerationAttempts)
                {
                    return StepResult.Failure(
                        FailureStep.TextGeneration.ToString(),
                        "TextContent exceeds max length after max attempts",
                        "TEXT_TOO_LONG");
                }

                continue;
            }

            // 3. Compute ContentHash
            var contentHash = _similarityCheck.ComputeContentHash(curiosity.TextContent);

            // 4. Check for duplicates (BR-004)
            if (await _similarityCheck.IsContentHashDuplicateAsync(contentHash, cancellationToken))
            {
                _logger.LogWarning(
                    "ContentHash duplicate detected on attempt {Attempt}: {ContentHash}",
                    attempt,
                    contentHash);

                if (attempt == MaxGenerationAttempts)
                {
                    return StepResult.Failure(
                        FailureStep.TextGeneration.ToString(),
                        "ContentHash duplicate detected after max attempts",
                        "HASH_DUPLICATE");
                }

                continue;
            }

            // 5. Check for similarity (BR-005)
            if (await _similarityCheck.IsSummarySimilarAsync(curiosity.Summary, threshold: 0.80, cancellationToken))
            {
                _logger.LogWarning(
                    "Summary similarity detected on attempt {Attempt}",
                    attempt);

                if (attempt == MaxGenerationAttempts)
                {
                    return StepResult.Failure(
                        FailureStep.TextGeneration.ToString(),
                        "Summary similarity detected after max attempts",
                        "SUMMARY_SIMILAR");
                }

                continue;
            }

            // 6. Create Post entity
            var post = new Post
            {
                CategoryId = context.CategoryId,
                SubcategoryId = context.SubcategoryId,
                TextContent = curiosity.TextContent,
                Summary = curiosity.Summary,
                Theme = curiosity.Theme,
                ContentHash = contentHash,
                SourceUrl = curiosity.SourceUrl,
                Status = PostStatus.Generated,
                Caption = $"{curiosity.TextContent}\n\nSource: {curiosity.SourceUrl}"
            };

            var createdPost = await _postRepository.CreateAsync(post, cancellationToken);
            context.PostId = createdPost.Id;

            _logger.LogInformation(
                "Text generation completed successfully: PostId={PostId}, attempt={Attempt}",
                createdPost.Id,
                attempt);

            return StepResult.Success();
        }

        // Should never reach here, but safety fallback
        return StepResult.Failure(
            FailureStep.TextGeneration.ToString(),
            "Unexpected failure in text generation",
            "UNEXPECTED_ERROR");
    }
}
