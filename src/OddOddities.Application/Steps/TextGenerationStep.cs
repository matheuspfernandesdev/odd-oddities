using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Ports;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Steps;

/// <summary>
/// Pipeline step for text generation via OpenRouter (RF-01).
/// Generates curiosity content, validates length, computes ContentHash,
/// checks for duplicates and similarity, then creates the Post entity.
/// Business rules: BR-001 (factual content), BR-002 (max 800 chars),
/// BR-004 (ContentHash duplicate), BR-005 (similarity threshold).
/// </summary>
public sealed class TextGenerationStep : IPipelineStep
{
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
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting text generation for {Category}/{Subcategory}",
            context.Selection.CategoryName,
            context.Selection.SubcategoryName);

        for (var attempt = 1; attempt <= PipelineConstants.MaxGenerationAttempts; attempt++)
        {
            _logger.LogDebug(
                "Text generation attempt {Attempt}/{MaxAttempts}",
                attempt,
                PipelineConstants.MaxGenerationAttempts);

            var curiosity = await _textGenerationPort.GenerateCuriosityAsync(
                context.Selection.CategoryName,
                context.Selection.SubcategoryName,
                cancellationToken);

            if (curiosity.TextContent.Length > _config.Value.MaxCaptionContentLength)
            {
                _logger.LogWarning(
                    "TextContent exceeds max length: {Length} > {MaxLength}",
                    curiosity.TextContent.Length,
                    _config.Value.MaxCaptionContentLength);

                if (attempt == PipelineConstants.MaxGenerationAttempts)
                {
                    return StepResult.Failure(
                        FailureStep.TextGeneration,
                        "TextContent exceeds max length after max attempts",
                        "TEXT_TOO_LONG");
                }

                continue;
            }

            var contentHash = _similarityCheck.ComputeContentHash(curiosity.TextContent);

            if (await _similarityCheck.IsContentHashDuplicateAsync(contentHash, cancellationToken))
            {
                _logger.LogWarning(
                    "ContentHash duplicate detected on attempt {Attempt}: {ContentHash}",
                    attempt,
                    contentHash);

                if (attempt == PipelineConstants.MaxGenerationAttempts)
                {
                    return StepResult.Failure(
                        FailureStep.TextGeneration,
                        "ContentHash duplicate detected after max attempts",
                        "HASH_DUPLICATE");
                }

                continue;
            }

            if (await _similarityCheck.IsSummarySimilarAsync(curiosity.Summary, PipelineConstants.DefaultSimilarityThreshold, cancellationToken))
            {
                _logger.LogWarning(
                    "Summary similarity detected on attempt {Attempt}",
                    attempt);

                if (attempt == PipelineConstants.MaxGenerationAttempts)
                {
                    return StepResult.Failure(
                        FailureStep.TextGeneration,
                        "Summary similarity detected after max attempts",
                        "SUMMARY_SIMILAR");
                }

                continue;
            }

            var post = new Post
            {
                CategoryId = context.Selection.CategoryId,
                SubcategoryId = context.Selection.SubcategoryId,
                TextContent = curiosity.TextContent,
                Summary = curiosity.Summary,
                Theme = curiosity.Theme,
                ContentHash = contentHash,
                SourceUrl = curiosity.SourceUrl,
                Status = PostStatus.Generated,
                Caption = $"{curiosity.TextContent}\n\nSource: {curiosity.SourceUrl}"
            };

            var createdPost = await _postRepository.CreateAsync(post, cancellationToken);

            context.Text = new TextContext(
                PostId: createdPost.Id,
                TextContent: curiosity.TextContent,
                Summary: curiosity.Summary,
                Theme: curiosity.Theme,
                ContentHash: contentHash,
                SourceUrl: curiosity.SourceUrl,
                Caption: post.Caption);

            _logger.LogInformation(
                "Text generation completed successfully: PostId={PostId}, attempt={Attempt}",
                createdPost.Id,
                attempt);

            return StepResult.Success();
        }

        return StepResult.Failure(
            FailureStep.TextGeneration,
            "Unexpected failure in text generation",
            "UNEXPECTED_ERROR");
    }
}
