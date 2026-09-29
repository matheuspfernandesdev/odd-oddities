using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Application.Pipeline;
using OddOddities.Application.Services;
using OddOddities.Domain.Constants;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Exceptions;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Steps;

/// <summary>
/// Pipeline step for image generation, processing, and storage (RF-01).
/// Builds a model candidate chain (preferred + dynamic catalog fallback), generates
/// the image, processes with ImageSharp (resize, watermark, JPEG), uploads to MinIO,
/// and updates the Post with image metadata. Each API/model failure advances to the
/// next candidate model (up to ModelSelection.MaxImageModelAttempts distinct models).
/// Cost is estimated pre-call against ModelSelection.MaxCostPerRunUsd and actual usage
/// is accumulated on the pipeline context. Every generation attempt is persisted to
/// GenerationAttempt.
/// Business rules: BR-008 (1080x1080 JPEG ~85 with watermark), BR-009 (MinIO quota).
/// </summary>
public sealed class ImageGenerationStep : IPipelineStep
{
    private readonly IImageGenerationPort _imageGenerationPort;
    private readonly IImageProcessingPort _imageProcessingPort;
    private readonly IObjectStoragePort _objectStoragePort;
    private readonly IPostRepository _postRepository;
    private readonly IGenerationAttemptRepository _generationAttemptRepository;
    private readonly IModelSelectionService _modelSelection;
    private readonly IOptions<AppConfiguration> _config;
    private readonly ILogger<ImageGenerationStep> _logger;

    public string StepName => "ImageGeneration";

    public ImageGenerationStep(
        IImageGenerationPort imageGenerationPort,
        IImageProcessingPort imageProcessingPort,
        IObjectStoragePort objectStoragePort,
        IPostRepository postRepository,
        IGenerationAttemptRepository generationAttemptRepository,
        IModelSelectionService modelSelection,
        IOptions<AppConfiguration> config,
        ILogger<ImageGenerationStep> logger)
    {
        _imageGenerationPort = imageGenerationPort ?? throw new ArgumentNullException(nameof(imageGenerationPort));
        _imageProcessingPort = imageProcessingPort ?? throw new ArgumentNullException(nameof(imageProcessingPort));
        _objectStoragePort = objectStoragePort ?? throw new ArgumentNullException(nameof(objectStoragePort));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _generationAttemptRepository = generationAttemptRepository ?? throw new ArgumentNullException(nameof(generationAttemptRepository));
        _modelSelection = modelSelection ?? throw new ArgumentNullException(nameof(modelSelection));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var text = context.Text
            ?? throw new InvalidOperationException("ImageGenerationStep requires a Text context.");

        var settings = _config.Value.ModelSelection;

        _logger.LogInformation(
            "Starting image generation for PostId={PostId}, theme={Theme}",
            text.PostId,
            text.Theme);

        var chain = await _modelSelection.GetImageChainAsync(cancellationToken);

        if (chain.Count == 0)
        {
            return StepResult.Failure(
                FailureStep.ImageGeneration,
                "No image models available (empty chain and no configured ImageModelId)",
                "NO_MODELS_AVAILABLE");
        }

        ImageGenerationResult? generated = null;
        var attemptNumber = 0;

        foreach (var candidate in chain)
        {
            var estimatedCost = ModelCostEstimator.EstimateImageCostPerRequest(candidate);

            if (!ModelCostEstimator.FitsBudget(
                    context.AccumulatedCostUsd,
                    estimatedCost,
                    settings.MaxCostPerRunUsd))
            {
                _logger.LogError(
                    "Image generation budget exceeded: accumulated={Accumulated} + estimated={Estimated} > max={Max}",
                    context.AccumulatedCostUsd,
                    estimatedCost,
                    settings.MaxCostPerRunUsd);

                return StepResult.Failure(
                    FailureStep.ImageGeneration,
                    $"Image generation budget exceeded: {context.AccumulatedCostUsd} + {estimatedCost} > {settings.MaxCostPerRunUsd} USD",
                    "BUDGET_EXCEEDED");
            }

            attemptNumber++;

            try
            {
                generated = await _imageGenerationPort.GenerateImageAsync(
                    text.Theme,
                    candidate.Id,
                    cancellationToken);
            }
            catch (Exception ex) when (IsModelLevelFailure(ex, cancellationToken))
            {
                _logger.LogWarning(
                    ex,
                    "Image model {ModelId} failed on attempt {Attempt}: advancing to next candidate",
                    candidate.Id,
                    attemptNumber);

                await RecordAttemptAsync(
                    text.PostId,
                    attemptNumber,
                    candidate.Id,
                    AttemptStatus.Error,
                    Truncate(ex.Message, 255),
                    costUsd: null,
                    durationMs: 0,
                    cancellationToken);

                continue;
            }

            var actualCost = generated.CostUsd ?? estimatedCost;
            context.AccumulatedCostUsd += actualCost;

            await RecordAttemptAsync(
                text.PostId,
                attemptNumber,
                candidate.Id,
                AttemptStatus.Success,
                rejectionReason: null,
                actualCost,
                generated.DurationMs,
                cancellationToken);

            _logger.LogInformation(
                "Image generated: PostId={PostId}, ModelId={ModelId}, SizeBytes={SizeBytes}, CostUsd={CostUsd}",
                text.PostId,
                candidate.Id,
                generated.ImageBytes.Length,
                actualCost);

            break;
        }

        if (generated is null)
        {
            return StepResult.Failure(
                FailureStep.ImageGeneration,
                $"All {chain.Count} image model candidate(s) failed",
                "ALL_MODELS_FAILED");
        }

        try
        {
            var imageData = generated.ImageBytes;

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
                "Image processing/storage failed for PostId={PostId}",
                text.PostId);

            return StepResult.Failure(
                FailureStep.ImageGeneration,
                $"Image generation failed: {ex.Message}",
                ex.GetType().Name);
        }
    }

    /// <summary>
    /// Model-level failures (API/transport/invalid model) advance the fallback chain.
    /// Caller cancellation always propagates.
    /// </summary>
    private static bool IsModelLevelFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex is OpenRouterModelException
            or HttpRequestException
            or TaskCanceledException
            or InvalidOperationException;
    }

    private async Task RecordAttemptAsync(
        long postId,
        int attemptNumber,
        string modelId,
        AttemptStatus status,
        string? rejectionReason,
        decimal? costUsd,
        long durationMs,
        CancellationToken cancellationToken)
    {
        try
        {
            await _generationAttemptRepository.CreateAsync(
                new GenerationAttempt
                {
                    PostId = postId,
                    AttemptNumber = attemptNumber,
                    ModelId = modelId.Length > 120 ? modelId[..120] : modelId,
                    Status = status,
                    RejectionReason = rejectionReason,
                    CostUsd = costUsd,
                    DurationMs = durationMs
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit must never break the generation pipeline.
            _logger.LogWarning(
                ex,
                "Failed to record image generation attempt {Attempt} for model {ModelId}",
                attemptNumber,
                modelId);
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
