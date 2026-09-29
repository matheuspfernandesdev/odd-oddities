using Microsoft.Extensions.Logging;
using OddOddities.Application.Pipeline;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Steps;

/// <summary>
/// Pipeline step for publishing to Instagram via Meta Graph API (RF-01).
/// Generates presigned URL, creates the media container, waits for the container to be
/// FINISHED, publishes the media, resolves the permalink, and persists the Publication record.
/// Business rules: BR-011 (publication recorded), BR-013 (Status + PublishedAt).
/// </summary>
public sealed class PublicationStep : IPipelineStep
{
    private readonly IPresignedUrlPort _presignedUrlPort;
    private readonly IInstagramPublishingPort _instagramPublishingPort;
    private readonly IPostRepository _postRepository;
    private readonly IPublicationRepository _publicationRepository;
    private readonly ILogger<PublicationStep> _logger;

    public string StepName => "InstagramApi";

    public PublicationStep(
        IPresignedUrlPort presignedUrlPort,
        IInstagramPublishingPort instagramPublishingPort,
        IPostRepository postRepository,
        IPublicationRepository publicationRepository,
        ILogger<PublicationStep> logger)
    {
        _presignedUrlPort = presignedUrlPort ?? throw new ArgumentNullException(nameof(presignedUrlPort));
        _instagramPublishingPort = instagramPublishingPort ?? throw new ArgumentNullException(nameof(instagramPublishingPort));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _publicationRepository = publicationRepository ?? throw new ArgumentNullException(nameof(publicationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var text = context.Text
            ?? throw new InvalidOperationException("PublicationStep requires a Text context.");
        var image = context.Image
            ?? throw new InvalidOperationException("PublicationStep requires an Image context.");

        _logger.LogInformation(
            "Starting publication for PostId={PostId}",
            text.PostId);

        using var stepTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepTimeout.CancelAfter(TimeSpan.FromSeconds(PipelineConstants.PublicationStepTimeoutSeconds));
        var ct = stepTimeout.Token;

        var publication = new Publication
        {
            PostId = text.PostId,
            AttemptCount = 1
        };

        try
        {
            var presignedUrl = await _presignedUrlPort.GeneratePresignedUrlAsync(
                image.ImageObjectKey,
                ct);

            _logger.LogInformation(
                "Presigned URL generated for PostId={PostId}",
                text.PostId);

            var post = await _postRepository.GetByIdAsync(text.PostId, ct);
            if (post is null)
            {
                _logger.LogError("Post {PostId} not found for publication", text.PostId);
                return StepResult.Failure(
                    FailureStep.InstagramApi,
                    $"Post {text.PostId} not found",
                    "POST_NOT_FOUND");
            }

            var containerId = await _instagramPublishingPort.CreateMediaContainerAsync(
                presignedUrl,
                post.Caption,
                ct);

            publication.MetaMediaId = containerId;

            _logger.LogInformation(
                "Media container created: containerId={ContainerId}",
                containerId);

            var (isReady, timedOut, containerStatus) = await WaitForContainerReadyAsync(containerId, ct);
            if (!isReady)
            {
                var errorCode = timedOut ? "CONTAINER_TIMEOUT" : "CONTAINER_FAILED";
                var reason = timedOut
                    ? $"Media container not ready after {PipelineConstants.MaxContainerPollingAttempts} attempts"
                    : $"Media container status: {containerStatus}";

                _logger.LogError(
                    "Media container {ContainerId} not publishable ({ErrorCode}): {Reason}",
                    containerId,
                    errorCode,
                    reason);

                await PersistPublicationAsync(
                    publication,
                    timedOut ? "TIMEOUT" : "ERROR",
                    containerStatus,
                    permalink: null);

                return StepResult.Failure(FailureStep.InstagramApi, reason, errorCode);
            }

            var mediaId = await _instagramPublishingPort.PublishMediaAsync(containerId, ct);

            publication.MetaMediaId = mediaId;

            _logger.LogInformation(
                "Media published: mediaId={MediaId}",
                mediaId);

            string? permalink = null;
            string? mediaError = null;

            for (var attempt = 1; attempt <= PipelineConstants.MaxPermalinkPollingAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PipelineConstants.PollingIntervalSeconds), ct);
                }

                var status = await _instagramPublishingPort.GetMediaStatusAsync(mediaId, ct);

                _logger.LogInformation(
                    "Media status attempt {Attempt}/{MaxAttempts}: mediaId={MediaId}, status={Status}, statusCode={StatusCode}",
                    attempt,
                    PipelineConstants.MaxPermalinkPollingAttempts,
                    mediaId,
                    status.Status,
                    status.StatusCode);

                if (status.StatusCode is "ERROR" or "EXPIRED")
                {
                    mediaError = status.StatusCode;
                    break;
                }

                permalink = status.Permalink;
                if (!string.IsNullOrEmpty(permalink))
                {
                    break;
                }
            }

            if (mediaError is not null)
            {
                _logger.LogError(
                    "Publication failed with status {StatusCode}: PostId={PostId}, mediaId={MediaId}",
                    mediaError,
                    text.PostId,
                    mediaId);

                await PersistPublicationAsync(publication, "ERROR", mediaError, permalink: null);

                return StepResult.Failure(
                    FailureStep.InstagramApi,
                    $"Publication failed with status: {mediaError}",
                    "PUBLICATION_FAILED");
            }

            if (string.IsNullOrEmpty(permalink))
            {
                // media_publish already returned the media id, so the post is live even if
                // the permalink is not readable yet. Failing here would risk a duplicate post.
                _logger.LogWarning(
                    "Published media {MediaId} has no permalink yet: PostId={PostId}",
                    mediaId,
                    text.PostId);
            }

            post.Status = PostStatus.Published;
            post.PublishedAt = DateTime.UtcNow;
            post.UpdatedAt = DateTime.UtcNow;

            await _postRepository.UpdateAsync(post, ct);

            await PersistPublicationAsync(publication, "PUBLISHED", "PUBLISHED", permalink);

            context.Publication = new PublicationContext(
                MetaMediaId: mediaId,
                MetaPermalink: permalink ?? string.Empty,
                MetaMediaStatus: "PUBLISHED",
                MetaMediaStatusCode: "PUBLISHED");

            _logger.LogInformation(
                "Publication completed successfully: PostId={PostId}, mediaId={MediaId}",
                text.PostId,
                mediaId);

            return StepResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: leave the Post untouched so the run can be resumed later.
            throw;
        }
        catch (OperationCanceledException) when (stepTimeout.IsCancellationRequested)
        {
            _logger.LogError(
                "Publication step timed out after {TimeoutSeconds}s: PostId={PostId}",
                PipelineConstants.PublicationStepTimeoutSeconds,
                text.PostId);

            await PersistPublicationAsync(publication, "TIMEOUT", "TIMEOUT", permalink: null);

            return StepResult.Failure(
                FailureStep.InstagramApi,
                $"Publication step timed out after {PipelineConstants.PublicationStepTimeoutSeconds}s",
                "STEP_TIMEOUT");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Publication failed for PostId={PostId}",
                text.PostId);

            await PersistPublicationAsync(publication, "ERROR", "ERROR", permalink: null);

            return StepResult.Failure(
                FailureStep.InstagramApi,
                $"Publication failed: {ex.Message}",
                ex.GetType().Name);
        }
    }

    /// <summary>
    /// Polls the media container until Instagram reports it as publishable.
    /// The container must be FINISHED before media_publish, otherwise the API rejects the call.
    /// </summary>
    private async Task<(bool IsReady, bool TimedOut, string StatusCode)> WaitForContainerReadyAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= PipelineConstants.MaxContainerPollingAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(PipelineConstants.PollingIntervalSeconds), cancellationToken);
            }

            var statusCode = await _instagramPublishingPort.GetContainerStatusAsync(
                containerId,
                cancellationToken);

            _logger.LogInformation(
                "Media container status attempt {Attempt}/{MaxAttempts}: containerId={ContainerId}, statusCode={StatusCode}",
                attempt,
                PipelineConstants.MaxContainerPollingAttempts,
                containerId,
                statusCode);

            if (statusCode is "FINISHED" or "PUBLISHED")
            {
                return (true, false, statusCode);
            }

            if (statusCode is "ERROR" or "EXPIRED")
            {
                return (false, false, statusCode);
            }
        }

        return (false, true, "TIMEOUT");
    }

    /// <summary>
    /// Upserts the Publication record for the Post (one row per Post, unique index on PostId).
    /// Persistence failures are logged and never mask the publication outcome.
    /// </summary>
    private async Task PersistPublicationAsync(
        Publication publication,
        string status,
        string statusCode,
        string? permalink)
    {
        if (string.IsNullOrEmpty(publication.MetaMediaId))
        {
            // Nothing ever reached the Meta API; the Post failure reason already explains it.
            return;
        }

        publication.MetaMediaStatus = status;
        publication.MetaMediaStatusCode = statusCode;
        publication.MetaPermalink = permalink;
        publication.LastCheckedAt = DateTime.UtcNow;
        publication.UpdatedAt = DateTime.UtcNow;

        // The step may be unwinding because its own timeout fired, so persistence must not
        // depend on the cancelled token.
        var ct = CancellationToken.None;

        try
        {
            var existing = await _publicationRepository.GetByPostIdAsync(publication.PostId, ct);
            if (existing is null)
            {
                await _publicationRepository.CreateAsync(publication, ct);
                return;
            }

            existing.MetaMediaId = publication.MetaMediaId;
            existing.MetaMediaStatus = publication.MetaMediaStatus;
            existing.MetaMediaStatusCode = publication.MetaMediaStatusCode;
            existing.MetaPermalink = publication.MetaPermalink;
            existing.LastCheckedAt = publication.LastCheckedAt;
            existing.UpdatedAt = publication.UpdatedAt;
            existing.AttemptCount += 1;

            await _publicationRepository.UpdateAsync(existing, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist Publication for PostId={PostId}",
                publication.PostId);
        }
    }
}
