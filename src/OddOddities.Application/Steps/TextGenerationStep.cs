using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Ports;
using OddOddities.Application.Services;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Steps;

/// <summary>
/// Pipeline step for text generation via OpenRouter (RF-01).
/// Builds a model candidate chain (preferred + dynamic catalog fallback), generates
/// curiosity content, validates length, computes ContentHash, checks duplicates and
/// similarity, then creates the Post entity. Each API/model failure advances to the
/// next candidate model (up to ModelSelection.MaxTextModelAttempts distinct models);
/// content rejections retry on the same model up to PipelineConstants.MaxGenerationAttempts.
/// Transient API errors (408/429/5xx/timeouts) retry on the same model with
/// exponential backoff (ADR-007) before advancing the fallback chain.
/// Cost is estimated pre-call against PipelineContext.CostCeilingUsd (resolved once per
/// run by the orchestrator: ModelSelection.MaxCostPerRunUsd for image runs,
/// MaxCostPerVideoRunUsd for video runs) and actual usage is accumulated on the
/// pipeline context. Every attempt is persisted to GenerationAttempt.
/// Business rules: BR-001 (factual content), BR-002 (max 800 chars),
/// BR-004 (ContentHash duplicate), BR-005 (similarity threshold).
/// </summary>
public sealed class TextGenerationStep : IPipelineStep
{
    private readonly ITextGenerationPort _textGenerationPort;
    private readonly ISimilarityCheckPort _similarityCheck;
    private readonly IPostRepository _postRepository;
    private readonly IGenerationAttemptRepository _generationAttemptRepository;
    private readonly IModelSelectionService _modelSelection;
    private readonly ICommentSuggestionRepository _commentSuggestionRepository;
    private readonly IOptions<AppConfiguration> _config;
    private readonly ILogger<TextGenerationStep> _logger;

    public string StepName => "TextGeneration";

    public TextGenerationStep(
        ITextGenerationPort textGenerationPort,
        ISimilarityCheckPort similarityCheck,
        IPostRepository postRepository,
        IGenerationAttemptRepository generationAttemptRepository,
        IModelSelectionService modelSelection,
        ICommentSuggestionRepository commentSuggestionRepository,
        IOptions<AppConfiguration> config,
        ILogger<TextGenerationStep> logger)
    {
        _textGenerationPort = textGenerationPort ?? throw new ArgumentNullException(nameof(textGenerationPort));
        _similarityCheck = similarityCheck ?? throw new ArgumentNullException(nameof(similarityCheck));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _generationAttemptRepository = generationAttemptRepository ?? throw new ArgumentNullException(nameof(generationAttemptRepository));
        _modelSelection = modelSelection ?? throw new ArgumentNullException(nameof(modelSelection));
        _commentSuggestionRepository = commentSuggestionRepository ?? throw new ArgumentNullException(nameof(commentSuggestionRepository));
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

        var chain = await _modelSelection.GetTextChainAsync(cancellationToken);

        if (chain.Count == 0)
        {
            return StepResult.Failure(
                FailureStep.TextGeneration,
                "No text models available (empty chain and no configured TextModelId)",
                "NO_MODELS_AVAILABLE");
        }

        var attemptNumber = 0;
        string? lastRejectionCode = null;
        string? lastRejectionReason = null;

        foreach (var candidate in chain)
        {
            var modelContentAttempts = 0;
            var abandonModel = false;

            while (modelContentAttempts < PipelineConstants.MaxGenerationAttempts && !abandonModel)
            {
                var estimatedCost = ModelCostEstimator.EstimateTextCostPerRequest(candidate);

                if (!ModelCostEstimator.FitsBudget(
                        context.AccumulatedCostUsd,
                        estimatedCost,
                        context.CostCeilingUsd))
                {
                    _logger.LogError(
                        "Text generation budget exceeded: accumulated={Accumulated} + estimated={Estimated} > max={Max}",
                        context.AccumulatedCostUsd,
                        estimatedCost,
                        context.CostCeilingUsd);

                    return StepResult.Failure(
                        FailureStep.TextGeneration,
                        $"Text generation budget exceeded: {context.AccumulatedCostUsd} + {estimatedCost} > {context.CostCeilingUsd} USD",
                        "BUDGET_EXCEEDED");
                }

                attemptNumber++;
                modelContentAttempts++;

                TextGenerationResult result;

                // RF-21 AC1: when an accepted comment suggestion is present, generate from
                // the suggestion's theme/summary instead of the selected category/subcategory.
                // The suggestion is captured per-iteration so a rejection (below) can clear
                // context.Suggestion and let the same attempt loop fall back to the normal flow.
                var suggestion = context.Suggestion;
                var generationCategory = suggestion is not null
                    ? suggestion.Theme
                    : context.Selection.CategoryName;
                var generationSubcategory = suggestion is not null
                    ? suggestion.Summary
                    : context.Selection.SubcategoryName;

                try
                {
                    result = await _textGenerationPort.GenerateCuriosityAsync(
                        generationCategory,
                        generationSubcategory,
                        candidate.Id,
                        cancellationToken);
                }
                catch (Exception ex) when (IsTransientApiError(ex, cancellationToken))
                {
                    _logger.LogWarning(
                        ex,
                        "Transient API error for model {ModelId} on attempt {Attempt}/{MaxAttemptsOnModel}: retrying with backoff",
                        candidate.Id,
                        modelContentAttempts,
                        PipelineConstants.MaxGenerationAttempts);

                    await RecordAttemptAsync(
                        context,
                        attemptNumber,
                        candidateId: candidate.Id,
                        status: AttemptStatus.Error,
                        rejectionReason: Truncate(ex.Message, 255),
                        costUsd: null,
                        tokensIn: null,
                        tokensOut: null,
                        durationMs: 0,
                        postId: null,
                        cancellationToken);

                    lastRejectionCode = "TRANSIENT_API_ERROR";
                    lastRejectionReason = $"Transient API error on model {candidate.Id}: {ex.Message}";

                    if (modelContentAttempts >= PipelineConstants.MaxGenerationAttempts)
                    {
                        abandonModel = true;
                        break;
                    }

                    var delaySeconds = Math.Min(
                        PipelineConstants.RetryBaseDelaySeconds *
                        Math.Pow(PipelineConstants.RetryMultiplier, modelContentAttempts - 1),
                        PipelineConstants.RetryMaxDelaySeconds);

                    _logger.LogInformation(
                        "Waiting {DelaySeconds}s before retrying model {ModelId} ({Attempt}/{MaxAttemptsOnModel})",
                        delaySeconds,
                        candidate.Id,
                        modelContentAttempts,
                        PipelineConstants.MaxGenerationAttempts);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    continue;
                }
                catch (Exception ex) when (IsModelLevelFailure(ex, cancellationToken))
                {
                    _logger.LogWarning(
                        ex,
                        "Text model {ModelId} failed on attempt {Attempt}: advancing to next candidate",
                        candidate.Id,
                        attemptNumber);

                    await RecordAttemptAsync(
                        context,
                        attemptNumber,
                        candidateId: candidate.Id,
                        status: AttemptStatus.Error,
                        rejectionReason: Truncate(ex.Message, 255),
                        costUsd: null,
                        tokensIn: null,
                        tokensOut: null,
                        durationMs: 0,
                        postId: null,
                        cancellationToken);

                    lastRejectionCode = "ALL_MODELS_FAILED";
                    lastRejectionReason = $"Model {candidate.Id} failed: {ex.Message}";
                    abandonModel = true;
                    break;
                }

                var actualCost = result.CostUsd ?? estimatedCost;
                context.AccumulatedCostUsd += actualCost;

                if (result.TextContent.Length > _config.Value.MaxCaptionContentLength)
                {
                    _logger.LogWarning(
                        "TextContent exceeds max length: {Length} > {MaxLength} (model {ModelId})",
                        result.TextContent.Length,
                        _config.Value.MaxCaptionContentLength,
                        candidate.Id);

                    await RecordAttemptAsync(
                        context,
                        attemptNumber,
                        candidate.Id,
                        AttemptStatus.Rejected,
                        "TEXT_TOO_LONG",
                        actualCost,
                        result.TokensIn,
                        result.TokensOut,
                        result.DurationMs,
                        postId: null,
                        cancellationToken);

                    lastRejectionCode = "TEXT_TOO_LONG";
                    lastRejectionReason = "TextContent exceeds max length";

                    if (suggestion is not null &&
                        await TryRejectSuggestionAndFallbackAsync(
                            context, suggestion, "TEXT_TOO_LONG", cancellationToken))
                    {
                        attemptNumber--;
                        modelContentAttempts--;
                    }

                    continue;
                }

                var contentHash = _similarityCheck.ComputeContentHash(result.TextContent);

                if (await _similarityCheck.IsContentHashDuplicateAsync(contentHash, cancellationToken))
                {
                    _logger.LogWarning(
                        "ContentHash duplicate detected on attempt {Attempt}: {ContentHash}",
                        attemptNumber,
                        contentHash);

                    await RecordAttemptAsync(
                        context,
                        attemptNumber,
                        candidate.Id,
                        AttemptStatus.Rejected,
                        "HASH_DUPLICATE",
                        actualCost,
                        result.TokensIn,
                        result.TokensOut,
                        result.DurationMs,
                        postId: null,
                        cancellationToken);

                    lastRejectionCode = "HASH_DUPLICATE";
                    lastRejectionReason = "ContentHash duplicate detected";

                    if (suggestion is not null &&
                        await TryRejectSuggestionAndFallbackAsync(
                            context, suggestion, "HASH_DUPLICATE", cancellationToken))
                    {
                        attemptNumber--;
                        modelContentAttempts--;
                    }

                    continue;
                }

                if (await _similarityCheck.IsSummarySimilarAsync(
                        result.Summary,
                        PipelineConstants.DefaultSimilarityThreshold,
                        cancellationToken))
                {
                    _logger.LogWarning(
                        "Summary similarity detected on attempt {Attempt}",
                        attemptNumber);

                    await RecordAttemptAsync(
                        context,
                        attemptNumber,
                        candidate.Id,
                        AttemptStatus.Rejected,
                        "SUMMARY_SIMILAR",
                        actualCost,
                        result.TokensIn,
                        result.TokensOut,
                        result.DurationMs,
                        postId: null,
                        cancellationToken);

                    lastRejectionCode = "SUMMARY_SIMILAR";
                    lastRejectionReason = "Summary similarity detected";

                    if (suggestion is not null &&
                        await TryRejectSuggestionAndFallbackAsync(
                            context, suggestion, "SUMMARY_SIMILAR", cancellationToken))
                    {
                        attemptNumber--;
                        modelContentAttempts--;
                    }

                    continue;
                }

                // RF-21 AC3/AC4: when this text originated from a comment suggestion, attach
                // the source suggestion id and inject the "Suggested by @user" credit between
                // the text and the source line.
                long? sourceCommentSuggestionId = null;
                var caption = $"{result.TextContent}\n\nSource: {result.SourceUrl}";

                if (suggestion is not null)
                {
                    var suggestionEntity = await _commentSuggestionRepository.GetByCommentIdAsync(
                        suggestion.CommentId,
                        cancellationToken);

                    sourceCommentSuggestionId = suggestionEntity?.Id;
                    caption = $"{result.TextContent}\n\nSuggested by @{suggestion.AuthorUsername}\n\nSource: {result.SourceUrl}";
                }

                var post = new Post
                {
                    CategoryId = context.Selection.CategoryId,
                    SubcategoryId = context.Selection.SubcategoryId,
                    TextContent = result.TextContent,
                    Summary = result.Summary,
                    Theme = result.Theme,
                    ContentHash = contentHash,
                    SourceUrl = result.SourceUrl,
                    Status = PostStatus.Generated,
                    SourceCommentSuggestionId = sourceCommentSuggestionId,
                    Caption = caption
                };

                var createdPost = await _postRepository.CreateAsync(post, cancellationToken);

                await RecordAttemptAsync(
                    context,
                    attemptNumber,
                    candidate.Id,
                    AttemptStatus.Success,
                    rejectionReason: null,
                    actualCost,
                    result.TokensIn,
                    result.TokensOut,
                    result.DurationMs,
                    postId: createdPost.Id,
                    cancellationToken);

                context.Text = new TextContext(
                    PostId: createdPost.Id,
                    TextContent: result.TextContent,
                    Summary: result.Summary,
                    Theme: result.Theme,
                    ContentHash: contentHash,
                    SourceUrl: result.SourceUrl,
                    Caption: post.Caption);

                _logger.LogInformation(
                    "Text generation completed successfully: PostId={PostId}, ModelId={ModelId}, attempt={Attempt}, CostUsd={CostUsd}",
                    createdPost.Id,
                    candidate.Id,
                    attemptNumber,
                    actualCost);

                return StepResult.Success();
            }
        }

        if (lastRejectionCode is null || lastRejectionCode == "ALL_MODELS_FAILED")
        {
            return StepResult.Failure(
                FailureStep.TextGeneration,
                lastRejectionReason ?? "All text models failed",
                "ALL_MODELS_FAILED");
        }

        return StepResult.Failure(
            FailureStep.TextGeneration,
            $"{lastRejectionReason} after max attempts",
            lastRejectionCode);
    }

    /// <summary>
    /// Transient API errors (rate limit, server errors, timeouts, connection issues)
    /// are retried on the same model with exponential backoff (ADR-007) before the
    /// fallback chain advances. Caller cancellation always propagates.
    /// </summary>
    private static bool IsTransientApiError(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex switch
        {
            OpenRouterModelException modelException => IsTransientStatusCode(modelException.StatusCode),
            HttpRequestException httpException => IsTransientStatusCode((int?)httpException.StatusCode),
            TaskCanceledException => true,
            _ => false
        };
    }

    private static bool IsTransientStatusCode(int? statusCode)
        => statusCode is null or 408 or 429 or >= 500;

    /// <summary>
    /// Model-level failures (API/transport/invalid model/malformed output) advance the
    /// fallback chain. Caller cancellation always propagates.
    /// </summary>
    private static bool IsModelLevelFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex is OpenRouterModelException
            or CuriosityParsingException
            or HttpRequestException
            or TaskCanceledException
            or InvalidOperationException;
    }

    /// <summary>
    /// RF-21 AC2: when text generated from a comment suggestion is rejected by editorial
    /// validation (length/hash/similarity), marks the persisted <see cref="CommentSuggestion"/>
    /// as Rejected with the same reason and clears <see cref="PipelineContext.Suggestion"/> so
    /// the current attempt loop falls back to the normal category flow. The suggestion attempt
    /// does not consume one of the BR-006 generation attempts, so the caller rolls back its
    /// attempt counters when this returns true. Returns true when the fallback was armed.
    /// </summary>
    private async Task<bool> TryRejectSuggestionAndFallbackAsync(
        PipelineContext context,
        SuggestionContext suggestion,
        string rejectionReason,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _commentSuggestionRepository.GetByCommentIdAsync(
                suggestion.CommentId,
                cancellationToken);

            if (entity is not null)
            {
                entity.Classification = CommentClassification.Rejected;
                entity.RejectionReason = rejectionReason;
                await _commentSuggestionRepository.UpdateAsync(entity, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Suggestion for CommentId {CommentId} rejected ({Reason}) but no persisted row was found to mark Rejected",
                    suggestion.CommentId,
                    rejectionReason);
            }
        }
        catch (Exception ex)
        {
            // Marking the suggestion Rejected must never break the fallback to the normal flow.
            _logger.LogWarning(
                ex,
                "Failed to mark suggestion for CommentId {CommentId} as Rejected ({Reason}); falling back to category flow anyway",
                suggestion.CommentId,
                rejectionReason);
        }

        _logger.LogWarning(
            "Comment suggestion by @{Author} rejected ({Reason}); falling back to the normal category flow",
            suggestion.AuthorUsername,
            rejectionReason);

        context.Suggestion = null;
        return true;
    }

    private async Task RecordAttemptAsync(
        PipelineContext context,
        int attemptNumber,
        string candidateId,
        AttemptStatus status,
        string? rejectionReason,
        decimal? costUsd,
        int? tokensIn,
        int? tokensOut,
        long durationMs,
        long? postId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _generationAttemptRepository.CreateAsync(
                new GenerationAttempt
                {
                    PostId = postId,
                    AttemptNumber = attemptNumber,
                    ModelId = candidateId.Length > 120 ? candidateId[..120] : candidateId,
                    Status = status,
                    RejectionReason = rejectionReason,
                    CostUsd = costUsd,
                    TokensIn = tokensIn,
                    TokensOut = tokensOut,
                    DurationMs = durationMs
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit must never break the generation pipeline.
            _logger.LogWarning(
                ex,
                "Failed to record generation attempt {Attempt} for model {ModelId}",
                attemptNumber,
                candidateId);
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
