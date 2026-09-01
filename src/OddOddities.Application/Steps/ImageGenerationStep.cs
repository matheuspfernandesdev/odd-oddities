using Microsoft.Extensions.Logging;
using OddOddities.Application.Pipeline;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Steps;

/// <summary>
/// Pipeline step for image generation, processing, and storage (RF-01).
/// Generates image via OpenRouter, processes with ImageSharp (resize, watermark, JPEG),
/// uploads to MinIO, and updates the Post with image metadata.
/// Business rules: BR-008 (1080x1080 JPEG ~85 with watermark), BR-009 (MinIO quota).
/// </summary>
public sealed class ImageGenerationStep : IPipelineStep
{
    private readonly IImageGenerationPort _imageGenerationPort;
    private readonly IImageProcessingPort _imageProcessingPort;
    private readonly IObjectStoragePort _objectStoragePort;
    private readonly IPostRepository _postRepository;
    private readonly ILogger<ImageGenerationStep> _logger;

    public string StepName => "ImageGeneration";

    public ImageGenerationStep(
        IImageGenerationPort imageGenerationPort,
        IImageProcessingPort imageProcessingPort,
        IObjectStoragePort objectStoragePort,
        IPostRepository postRepository,
        ILogger<ImageGenerationStep> logger)
    {
        _imageGenerationPort = imageGenerationPort ?? throw new ArgumentNullException(nameof(imageGenerationPort));
        _imageProcessingPort = imageProcessingPort ?? throw new ArgumentNullException(nameof(imageProcessingPort));
        _objectStoragePort = objectStoragePort ?? throw new ArgumentNullException(nameof(objectStoragePort));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var text = context.Text
            ?? throw new InvalidOperationException("ImageGenerationStep requires a Text context.");

        _logger.LogInformation(
            "Starting image generation for PostId={PostId}, theme={Theme}",
            text.PostId,
            text.Theme);

        try
        {
            var imageData = await _imageGenerationPort.GenerateImageAsync(
                text.Theme,
                cancellationToken);

            _logger.LogInformation(
                "Image generated: {SizeBytes} bytes",
                imageData.Length);

            var processed = await _imageProcessingPort.ProcessImageAsync(
                imageData,
                cancellationToken);

            _logger.LogInformation(
                "Image processed: {Width}x{Height}, format={Format}",
                processed.Width,
                processed.Height,
                processed.Format);

            var currentUsage = await _objectStoragePort.GetBucketUsageBytesAsync(cancellationToken);
            var quotaBytes = StorageConstants.MinioDefaultQuotaBytes;
            var newTotal = currentUsage + processed.ImageData.Length;

            if (newTotal > quotaBytes)
            {
                _logger.LogError(
                    "MinIO quota exceeded: current={CurrentBytes}, new={NewTotal}, quota={QuotaBytes}",
                    currentUsage,
                    newTotal,
                    quotaBytes);

                return StepResult.Failure(
                    FailureStep.ImageStorage,
                    $"MinIO quota exceeded: {newTotal} bytes would exceed {quotaBytes} bytes limit",
                    "QUOTA_EXCEEDED");
            }

            var objectKey = Guid.NewGuid().ToString("N");
            await _objectStoragePort.PutObjectAsync(
                objectKey,
                processed.ImageData,
                "image/jpeg",
                cancellationToken);

            _logger.LogInformation(
                "Image uploaded to MinIO: key={ObjectKey}, size={SizeBytes}",
                objectKey,
                processed.ImageData.Length);

            var post = await _postRepository.GetByIdAsync(text.PostId, cancellationToken);
            if (post is null)
            {
                _logger.LogError("Post {PostId} not found when updating image metadata", text.PostId);
                return StepResult.Failure(
                    FailureStep.ImageStorage,
                    $"Post {text.PostId} not found",
                    "POST_NOT_FOUND");
            }

            post.ImageObjectKey = objectKey;
            post.ImageWidth = processed.Width;
            post.ImageHeight = processed.Height;
            post.ImageBytes = processed.ImageData.Length;
            post.Status = PostStatus.ImageProcessed;
            post.UpdatedAt = DateTime.UtcNow;

            await _postRepository.UpdateAsync(post, cancellationToken);

            context.Image = new ImageContext(
                ImageObjectKey: objectKey,
                Width: processed.Width,
                Height: processed.Height,
                Bytes: processed.ImageData.Length);

            _logger.LogInformation(
                "Image generation completed successfully: PostId={PostId}, key={ObjectKey}",
                text.PostId,
                objectKey);

            return StepResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Image generation failed for PostId={PostId}",
                text.PostId);

            return StepResult.Failure(
                FailureStep.ImageGeneration,
                $"Image generation failed: {ex.Message}",
                ex.GetType().Name);
        }
    }
}
