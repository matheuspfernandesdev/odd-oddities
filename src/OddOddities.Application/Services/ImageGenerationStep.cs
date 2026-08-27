using Microsoft.Extensions.Logging;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

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
        PipelineExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting image generation for PostId={PostId}, theme={Theme}",
            context.PostId,
            context.Theme);

        try
        {
            // 1. Generate image via OpenRouter
            var imageData = await _imageGenerationPort.GenerateImageAsync(
                context.Theme,
                cancellationToken);

            _logger.LogInformation(
                "Image generated: {SizeBytes} bytes",
                imageData.Length);

            // 2. Process image (resize, watermark, JPEG encoding)
            var processed = await _imageProcessingPort.ProcessImageAsync(
                imageData,
                cancellationToken);

            _logger.LogInformation(
                "Image processed: {Width}x{Height}, format={Format}",
                processed.Width,
                processed.Height,
                processed.Format);

            // 3. Check quota before upload (BR-009)
            var currentUsage = await _objectStoragePort.GetBucketUsageBytesAsync(cancellationToken);
            var quotaBytes = 21_474_836_480L; // 20 GB
            var newTotal = currentUsage + processed.ImageData.Length;

            if (newTotal > quotaBytes)
            {
                _logger.LogError(
                    "MinIO quota exceeded: current={CurrentBytes}, new={NewTotal}, quota={QuotaBytes}",
                    currentUsage,
                    newTotal,
                    quotaBytes);

                return StepResult.Failure(
                    FailureStep.ImageStorage.ToString(),
                    $"MinIO quota exceeded: {newTotal} bytes would exceed {quotaBytes} bytes limit",
                    "QUOTA_EXCEEDED");
            }

            // 4. Upload to MinIO
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

            // 5. Update Post with image metadata
            var post = await _postRepository.GetByIdAsync(context.PostId, cancellationToken);
            if (post is null)
            {
                _logger.LogError("Post {PostId} not found when updating image metadata", context.PostId);
                return StepResult.Failure(
                    FailureStep.ImageStorage.ToString(),
                    $"Post {context.PostId} not found",
                    "POST_NOT_FOUND");
            }

            post.ImageObjectKey = objectKey;
            post.ImageWidth = processed.Width;
            post.ImageHeight = processed.Height;
            post.ImageBytes = processed.ImageData.Length;
            post.Status = PostStatus.ImageProcessed;
            post.UpdatedAt = DateTime.UtcNow;

            await _postRepository.UpdateAsync(post, cancellationToken);

            context.ImageObjectKey = objectKey;

            _logger.LogInformation(
                "Image generation completed successfully: PostId={PostId}, key={ObjectKey}",
                context.PostId,
                objectKey);

            return StepResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Image generation failed for PostId={PostId}",
                context.PostId);

            return StepResult.Failure(
                FailureStep.ImageGeneration.ToString(),
                $"Image generation failed: {ex.Message}",
                ex.GetType().Name);
        }
    }
}
