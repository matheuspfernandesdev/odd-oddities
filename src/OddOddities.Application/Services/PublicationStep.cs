using Microsoft.Extensions.Logging;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Pipeline step for publishing to Instagram via Meta Graph API (RF-01).
/// Generates presigned URL, creates media container, publishes media,
/// polls for status, and persists Publication record.
/// Business rules: BR-011 (publication recorded), BR-013 (Status + PublishedAt).
/// </summary>
public sealed class PublicationStep : IPipelineStep
{
    private readonly IPresignedUrlPort _presignedUrlPort;
    private readonly IInstagramPublishingPort _instagramPublishingPort;
    private readonly IPostRepository _postRepository;
    private readonly IPublicationRepository _publicationRepository;
    private readonly ILogger<PublicationStep> _logger;

    private const int MaxPollingAttempts = 30;
    private const int PollingIntervalSeconds = 2;

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
        PipelineExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting publication for PostId={PostId}",
            context.PostId);

        try
        {
            // 1. Generate presigned URL (RF-05: 24h validity)
            var presignedUrl = await _presignedUrlPort.GeneratePresignedUrlAsync(
                context.ImageObjectKey,
                cancellationToken);

            _logger.LogInformation(
                "Presigned URL generated for PostId={PostId}",
                context.PostId);

            // 2. Create media container on Meta
            var post = await _postRepository.GetByIdAsync(context.PostId, cancellationToken);
            if (post is null)
            {
                _logger.LogError("Post {PostId} not found for publication", context.PostId);
                return StepResult.Failure(
                    FailureStep.InstagramApi.ToString(),
                    $"Post {context.PostId} not found",
                    "POST_NOT_FOUND");
            }

            var mediaId = await _instagramPublishingPort.CreateMediaContainerAsync(
                presignedUrl,
                post.Caption,
                cancellationToken);

            _logger.LogInformation(
                "Media container created: mediaId={MediaId}",
                mediaId);

            // 3. Publish media
            var publishResult = await _instagramPublishingPort.PublishMediaAsync(
                mediaId,
                cancellationToken);

            _logger.LogInformation(
                "Media published: mediaId={MediaId}",
                publishResult);

            // 4. Poll for status until Published/Error
            var publication = new Publication
            {
                PostId = context.PostId,
                MetaMediaId = publishResult,
                MetaMediaStatus = "PENDING",
                MetaMediaStatusCode = "PENDING",
                AttemptCount = 1
            };

            for (var attempt = 0; attempt < MaxPollingAttempts; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), cancellationToken);

                var status = await _instagramPublishingPort.GetMediaStatusAsync(
                    publishResult,
                    cancellationToken);

                publication.MetaMediaStatus = status.Status;
                publication.MetaMediaStatusCode = status.StatusCode;
                publication.LastCheckedAt = DateTime.UtcNow;

                _logger.LogDebug(
                    "Polling attempt {Attempt}: status={Status}, statusCode={StatusCode}",
                    attempt + 1,
                    status.Status,
                    status.StatusCode);

                if (status.StatusCode == "PUBLISHED")
                {
                    // Success: update Post status and persist Publication
                    post.Status = PostStatus.Published;
                    post.PublishedAt = DateTime.UtcNow;
                    post.UpdatedAt = DateTime.UtcNow;

                    await _postRepository.UpdateAsync(post, cancellationToken);

                    publication.MetaPermalink = status.Permalink;
                    publication.UpdatedAt = DateTime.UtcNow;

                    await _publicationRepository.CreateAsync(publication, cancellationToken);

                    _logger.LogInformation(
                        "Publication completed successfully: PostId={PostId}, mediaId={MediaId}",
                        context.PostId,
                        publishResult);

                    return StepResult.Success();
                }

                if (status.StatusCode == "ERROR")
                {
                    _logger.LogWarning(
                        "Publication failed with status ERROR: PostId={PostId}",
                        context.PostId);

                    // Persist failed publication
                    publication.MetaMediaStatus = "ERROR";
                    publication.MetaMediaStatusCode = "ERROR";
                    publication.UpdatedAt = DateTime.UtcNow;

                    await _publicationRepository.CreateAsync(publication, cancellationToken);

                    return StepResult.Failure(
                        FailureStep.InstagramApi.ToString(),
                        $"Publication failed with status: {status.StatusCode}",
                        "PUBLICATION_FAILED");
                }
            }

            // Polling timeout
            _logger.LogWarning(
                "Polling timeout after {MaxAttempts} attempts: PostId={PostId}",
                MaxPollingAttempts,
                context.PostId);

            publication.MetaMediaStatus = "TIMEOUT";
            publication.MetaMediaStatusCode = "TIMEOUT";
            publication.UpdatedAt = DateTime.UtcNow;

            await _publicationRepository.CreateAsync(publication, cancellationToken);

            return StepResult.Failure(
                FailureStep.InstagramApi.ToString(),
                $"Polling timeout after {MaxPollingAttempts} attempts",
                "POLLING_TIMEOUT");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Publication failed for PostId={PostId}",
                context.PostId);

            return StepResult.Failure(
                FailureStep.InstagramApi.ToString(),
                $"Publication failed: {ex.Message}",
                ex.GetType().Name);
        }
    }
}
