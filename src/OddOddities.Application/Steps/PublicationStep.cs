using Microsoft.Extensions.Logging;
using OddOddities.Application.Pipeline;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Steps;

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

        try
        {
            var presignedUrl = await _presignedUrlPort.GeneratePresignedUrlAsync(
                image.ImageObjectKey,
                cancellationToken);

            _logger.LogInformation(
                "Presigned URL generated for PostId={PostId}",
                text.PostId);

            var post = await _postRepository.GetByIdAsync(text.PostId, cancellationToken);
            if (post is null)
            {
                _logger.LogError("Post {PostId} not found for publication", text.PostId);
                return StepResult.Failure(
                    FailureStep.InstagramApi,
                    $"Post {text.PostId} not found",
                    "POST_NOT_FOUND");
            }

            var mediaId = await _instagramPublishingPort.CreateMediaContainerAsync(
                presignedUrl,
                post.Caption,
                cancellationToken);

            _logger.LogInformation(
                "Media container created: mediaId={MediaId}",
                mediaId);

            var publishResult = await _instagramPublishingPort.PublishMediaAsync(
                mediaId,
                cancellationToken);

            _logger.LogInformation(
                "Media published: mediaId={MediaId}",
                publishResult);

            var publication = new Publication
            {
                PostId = text.PostId,
                MetaMediaId = publishResult,
                MetaMediaStatus = "PENDING",
                MetaMediaStatusCode = "PENDING",
                AttemptCount = 1
            };

            for (var attempt = 0; attempt < PipelineConstants.MaxPollingAttempts; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(PipelineConstants.PollingIntervalSeconds), cancellationToken);

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
                    post.Status = PostStatus.Published;
                    post.PublishedAt = DateTime.UtcNow;
                    post.UpdatedAt = DateTime.UtcNow;

                    await _postRepository.UpdateAsync(post, cancellationToken);

                    publication.MetaPermalink = status.Permalink;
                    publication.UpdatedAt = DateTime.UtcNow;

                    await _publicationRepository.CreateAsync(publication, cancellationToken);

                    context.Publication = new PublicationContext(
                        MetaMediaId: publishResult,
                        MetaPermalink: status.Permalink,
                        MetaMediaStatus: status.Status,
                        MetaMediaStatusCode: status.StatusCode);

                    _logger.LogInformation(
                        "Publication completed successfully: PostId={PostId}, mediaId={MediaId}",
                        text.PostId,
                        publishResult);

                    return StepResult.Success();
                }

                if (status.StatusCode == "ERROR")
                {
                    _logger.LogWarning(
                        "Publication failed with status ERROR: PostId={PostId}",
                        text.PostId);

                    publication.MetaMediaStatus = "ERROR";
                    publication.MetaMediaStatusCode = "ERROR";
                    publication.UpdatedAt = DateTime.UtcNow;

                    await _publicationRepository.CreateAsync(publication, cancellationToken);

                    return StepResult.Failure(
                        FailureStep.InstagramApi,
                        $"Publication failed with status: {status.StatusCode}",
                        "PUBLICATION_FAILED");
                }
            }

            _logger.LogWarning(
                "Polling timeout after {MaxAttempts} attempts: PostId={PostId}",
                PipelineConstants.MaxPollingAttempts,
                text.PostId);

            publication.MetaMediaStatus = "TIMEOUT";
            publication.MetaMediaStatusCode = "TIMEOUT";
            publication.UpdatedAt = DateTime.UtcNow;

            await _publicationRepository.CreateAsync(publication, cancellationToken);

            return StepResult.Failure(
                FailureStep.InstagramApi,
                $"Polling timeout after {PipelineConstants.MaxPollingAttempts} attempts",
                "POLLING_TIMEOUT");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Publication failed for PostId={PostId}",
                text.PostId);

            return StepResult.Failure(
                FailureStep.InstagramApi,
                $"Publication failed: {ex.Message}",
                ex.GetType().Name);
        }
    }
}
