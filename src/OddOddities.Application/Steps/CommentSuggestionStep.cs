using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Services;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Steps;

/// <summary>
/// First pipeline step (RF-20): reads the comments of the latest published posts,
/// classifies the new ones with one OpenRouter text call (existing free chain) and
/// accepts at most one theme suggestion per run.
/// <para>
/// Short-circuits with <see cref="StepResult.Skipped"/> (RF-13) when Comments.Enabled is
/// false, on video runs, when there are no published posts with a MetaMediaId, when there
/// are no new comments, on Meta permission errors (R14 fallback) and on any AI
/// classification failure — reading comments never fails the day.
/// </para>
/// <para>
/// Every new comment (non-empty CommentId) is persisted exactly once in
/// CommentSuggestions, so the CommentId unique index guarantees it is never reprocessed.
/// Editorial validation (hash/similarity) is NOT done here: it happens inside
/// TextGenerationStep (RF-21), when there is generated text to hash.
/// </para>
/// </summary>
public sealed class CommentSuggestionStep : IPipelineStep
{
    /// <summary>Rejection reason for valid suggestions beyond the first one of the run.</summary>
    public const string LimitOneSuggestionReason = "LIMIT_ONE_SUGGESTION_PER_RUN";

    /// <summary>Rejection reason when the model flagged a suggestion without a usable theme.</summary>
    public const string MissingThemeReason = "MISSING_THEME";

    /// <summary>
    /// Exact warning required by RF-20 AC8 (R14 fallback). Exposed as a constant so tests
    /// assert the literal instead of duplicating the em dash in the test file.
    /// </summary>
    public const string PermissionMissingWarning =
        "comment permission missing — skipping comment suggestion step";

    /// <summary>
    /// Meta error body codes that mean "the token cannot read comments" (R14):
    /// 10 (permission denied), 190 (invalid/expired token), 3 (unknown/insufficient permission).
    /// Matches both compact (<c>"code":190</c>) and spaced (<c>"code": 190</c>) JSON bodies.
    /// </summary>
    private static readonly Regex MetaPermissionCodePattern = new(
        @"['""]?code['""]?\s*[:=]\s*(10|190|3)(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IPostRepository _postRepository;
    private readonly IMediaCommentPort _mediaCommentPort;
    private readonly ICommentSuggestionRepository _commentSuggestionRepository;
    private readonly ICommentClassificationPort _commentClassificationPort;
    private readonly IModelSelectionService _modelSelection;
    private readonly IOptions<AppConfiguration> _config;
    private readonly ILogger<CommentSuggestionStep> _logger;

    public string StepName => "CommentSuggestion";

    public CommentSuggestionStep(
        IPostRepository postRepository,
        IMediaCommentPort mediaCommentPort,
        ICommentSuggestionRepository commentSuggestionRepository,
        ICommentClassificationPort commentClassificationPort,
        IModelSelectionService modelSelection,
        IOptions<AppConfiguration> config,
        ILogger<CommentSuggestionStep> logger)
    {
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _mediaCommentPort = mediaCommentPort ?? throw new ArgumentNullException(nameof(mediaCommentPort));
        _commentSuggestionRepository = commentSuggestionRepository ?? throw new ArgumentNullException(nameof(commentSuggestionRepository));
        _commentClassificationPort = commentClassificationPort ?? throw new ArgumentNullException(nameof(commentClassificationPort));
        _modelSelection = modelSelection ?? throw new ArgumentNullException(nameof(modelSelection));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Value.Comments.Enabled)
        {
            _logger.LogInformation(
                "Comment suggestion step skipped: Comments.Enabled is false");
            return StepResult.Skipped();
        }

        if (context.IsVideoRun)
        {
            _logger.LogInformation(
                "Comment suggestion step skipped: video run");
            return StepResult.Skipped();
        }

        var lookbackPosts = _config.Value.Comments.LookbackPosts;

        var fetchedMediaIds = await _postRepository.GetLatestPublishedMediaIdsAsync(
            lookbackPosts,
            cancellationToken);

        // AC2 is about published posts WITH a MetaMediaId: drop null/empty ids so a
        // malformed Publication row cannot make GetCommentsAsync throw ArgumentException
        // (the port validates the media id) and abort comment reading for the whole day.
        var mediaIds = fetchedMediaIds
            .Where(mediaId => !string.IsNullOrWhiteSpace(mediaId))
            .ToList();

        if (mediaIds.Count != fetchedMediaIds.Count)
        {
            _logger.LogWarning(
                "Discarding {DiscardedCount} published posts without MetaMediaId (lookback {LookbackPosts})",
                fetchedMediaIds.Count - mediaIds.Count,
                lookbackPosts);
        }

        if (mediaIds.Count == 0)
        {
            _logger.LogInformation(
                "Comment suggestion step skipped: no published posts with MetaMediaId (lookback {LookbackPosts})",
                lookbackPosts);
            return StepResult.Skipped();
        }

        var fetched = await ReadCommentsAsync(mediaIds, cancellationToken);

        if (fetched is null)
        {
            // Meta failure (permission or otherwise): warning already logged, the step is
            // skipped and the pipeline continues (AC8).
            return StepResult.Skipped();
        }

        if (fetched.Count == 0)
        {
            _logger.LogInformation(
                "Comment suggestion step skipped: no comments found across {MediaCount} medias",
                mediaIds.Count);
            return StepResult.Skipped();
        }

        _logger.LogInformation(
            "Comment suggestion step fetched {CommentCount} comments across {MediaCount} medias",
            fetched.Count,
            mediaIds.Count);

        var newComments = await FilterProcessedAsync(fetched, cancellationToken);

        if (newComments.Count == 0)
        {
            _logger.LogInformation(
                "Comment suggestion step skipped: no new comments ({CommentCount} already processed)",
                fetched.Count);
            return StepResult.Skipped();
        }

        var classifications = await ClassifyAsync(newComments, cancellationToken);

        if (classifications is null)
        {
            // AI failure: no suggestion this run, nothing persisted (the comments stay
            // "new" and are retried on the next run), pipeline continues normally (AC6).
            return StepResult.Skipped();
        }

        return await DecideAsync(context, newComments, classifications, cancellationToken);
    }

    /// <summary>
    /// Reads the comments of every media in the lookback window. Media with an empty
    /// CommentId payload are dropped (RF-19 MapComment fallback), so only usable ids
    /// reach persistence. Returns null when Meta failed (permission or otherwise): any
    /// Meta failure skips the step, reading comments never fails the run (AC8).
    /// </summary>
    private async Task<IReadOnlyList<(string MediaId, MediaComment Comment)>?> ReadCommentsAsync(
        IReadOnlyList<string> mediaIds,
        CancellationToken cancellationToken)
    {
        var fetched = new List<(string MediaId, MediaComment Comment)>();

        try
        {
            foreach (var mediaId in mediaIds)
            {
                var mediaComments = await _mediaCommentPort.GetCommentsAsync(mediaId, cancellationToken);

                foreach (var comment in mediaComments)
                {
                    if (string.IsNullOrWhiteSpace(comment.CommentId))
                    {
                        _logger.LogWarning(
                            "Discarding comment without CommentId on media {MediaId}",
                            mediaId);
                        continue;
                    }

                    fetched.Add((mediaId, comment));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsMetaPermissionError(ex))
            {
                _logger.LogWarning(ex, PermissionMissingWarning);
            }
            else
            {
                _logger.LogWarning(ex, "Meta comment read failed — skipping comment suggestion step");
            }

            return null;
        }

        return fetched;
    }

    /// <summary>
    /// Drops comments whose CommentId already exists in CommentSuggestions (idempotency,
    /// RF-19) and duplicates inside the fetched batch.
    /// </summary>
    private async Task<IReadOnlyList<(string MediaId, MediaComment Comment)>> FilterProcessedAsync(
        IReadOnlyList<(string MediaId, MediaComment Comment)> fetched,
        CancellationToken cancellationToken)
    {
        var newComments = new List<(string MediaId, MediaComment Comment)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in fetched)
        {
            if (!seen.Add(entry.Comment.CommentId))
            {
                continue;
            }

            var alreadyProcessed = await _commentSuggestionRepository.ExistsByCommentIdAsync(
                entry.Comment.CommentId,
                cancellationToken);

            if (alreadyProcessed)
            {
                continue;
            }

            newComments.Add(entry);
        }

        return newComments;
    }

    /// <summary>
    /// One OpenRouter text call over the existing free chain (first candidate), classifying
    /// the whole batch. Returns null when no suggestion can be produced this run (empty
    /// chain or AI failure) — the caller then skips without persisting anything.
    /// </summary>
    private async Task<IReadOnlyList<CommentClassificationResult>?> ClassifyAsync(
        IReadOnlyList<(string MediaId, MediaComment Comment)> newComments,
        CancellationToken cancellationToken)
    {
        try
        {
            var chain = await _modelSelection.GetTextChainAsync(cancellationToken);

            if (chain.Count == 0)
            {
                _logger.LogWarning(
                    "No text model available for comment classification — continuing without a suggestion");
                return null;
            }

            return await _commentClassificationPort.ClassifyCommentsAsync(
                newComments.Select(entry => entry.Comment).ToList(),
                chain[0].Id,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Comment classification failed — continuing without a suggestion");
            return null;
        }
    }

    /// <summary>
    /// Persists every new comment (NotSuggestion or Accepted) and accepts at most one
    /// suggestion per run. The entity is persisted BEFORE the context is marked, so a
    /// suggestion is only used downstream when its audit row exists (RF-21 FK).
    /// </summary>
    private async Task<StepResult> DecideAsync(
        PipelineContext context,
        IReadOnlyList<(string MediaId, MediaComment Comment)> newComments,
        IReadOnlyList<CommentClassificationResult> classifications,
        CancellationToken cancellationToken)
    {
        var acceptedSuggestion = false;

        foreach (var entry in newComments)
        {
            var comment = entry.Comment;
            var classification = classifications.FirstOrDefault(
                result => string.Equals(result.CommentId, comment.CommentId, StringComparison.Ordinal));

            var isSuggestion = classification is not null
                && classification.IsSuggestion
                && !string.IsNullOrWhiteSpace(classification.Theme);

            CommentClassification status;
            string? rejectionReason = null;

            if (!isSuggestion)
            {
                status = CommentClassification.NotSuggestion;

                if (classification is { IsSuggestion: true })
                {
                    // The model flagged a suggestion but gave no usable theme: audited as
                    // NotSuggestion with the reason, so it is never reprocessed either.
                    rejectionReason = MissingThemeReason;
                }
            }
            else if (!acceptedSuggestion)
            {
                status = CommentClassification.Accepted;
            }
            else
            {
                // AC5: only the first valid suggestion of the run becomes Accepted; the
                // others stay NotSuggestion with the reason so idempotency and audit remain
                // honest (they are never reprocessed).
                status = CommentClassification.NotSuggestion;
                rejectionReason = LimitOneSuggestionReason;
            }

            await _commentSuggestionRepository.CreateAsync(
                new CommentSuggestion
                {
                    CommentId = comment.CommentId,
                    MediaId = entry.MediaId,
                    AuthorUsername = comment.AuthorUsername,
                    CommentText = comment.Text,
                    Classification = status,
                    RejectionReason = rejectionReason,
                    ProcessedAt = DateTime.UtcNow
                },
                cancellationToken);

            if (status != CommentClassification.Accepted)
            {
                continue;
            }

            context.Suggestion = new SuggestionContext(
                Theme: classification!.Theme,
                Summary: classification.Summary,
                AuthorUsername: comment.AuthorUsername,
                CommentId: comment.CommentId,
                SourceCommentText: comment.Text);

            acceptedSuggestion = true;

            _logger.LogInformation(
                "Comment suggestion accepted: CommentId={CommentId}, MediaId={MediaId}, Author={AuthorUsername}, Theme={Theme}",
                comment.CommentId,
                entry.MediaId,
                comment.AuthorUsername,
                classification.Theme);
        }

        _logger.LogInformation(
            "Comment suggestion step processed {CommentCount} new comments: Accepted={AcceptedCount}, SuggestionActive={HasSuggestion}",
            newComments.Count,
            acceptedSuggestion ? 1 : 0,
            context.Suggestion is not null);

        return StepResult.Success();
    }

    /// <summary>
    /// R14 fallback: a Meta permission failure (HTTP 403, or an error body carrying code
    /// 10/190/3 — the shape EnsureSuccessAsync embeds in the HttpRequestException message)
    /// means the token cannot read comments. Other Meta errors are treated the same way
    /// (skip), only with a different log line.
    /// </summary>
    private static bool IsMetaPermissionError(Exception ex)
    {
        Exception? current = ex;

        while (current is not null)
        {
            if (current is HttpRequestException httpException)
            {
                if (httpException.StatusCode == HttpStatusCode.Forbidden)
                {
                    return true;
                }

                if (MetaPermissionCodePattern.IsMatch(httpException.Message))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }
}
