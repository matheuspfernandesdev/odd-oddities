using System.Diagnostics;
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
/// Pipeline step for asynchronous video generation and storage (RF-17).
/// Mirrors <see cref="ImageGenerationStep"/>: builds a video model candidate chain
/// (preferred + dynamic catalog fallback), generates the MP4 through the submit/poll/
/// download flow, enforces the pre-call budget (cost/second x duration) against
/// PipelineContext.CostCeilingUsd, uploads to MinIO (video/mp4, BR-009 quota) and updates
/// the Post with video metadata. Each model failure advances to the next candidate
/// (up to ModelSelection.MaxVideoModelAttempts distinct models) and every attempt is
/// persisted to GenerationAttempt. Runs only on video executions: on an image run the step
/// short-circuits with StepResult.Skipped (RF-13/RF-15).
/// </summary>
public sealed class VideoGenerationStep : IPipelineStep
{
    private readonly IVideoGenerationPort _videoGenerationPort;
    private readonly IObjectStoragePort _objectStoragePort;
    private readonly IPostRepository _postRepository;
    private readonly IGenerationAttemptRepository _generationAttemptRepository;
    private readonly IModelSelectionService _modelSelection;
    private readonly AppConfiguration _config;
    private readonly ILogger<VideoGenerationStep> _logger;

    public string StepName => "VideoGeneration";

    public VideoGenerationStep(
        IVideoGenerationPort videoGenerationPort,
        IObjectStoragePort objectStoragePort,
        IPostRepository postRepository,
        IGenerationAttemptRepository generationAttemptRepository,
        IModelSelectionService modelSelection,
        IOptions<AppConfiguration> config,
        ILogger<VideoGenerationStep> logger)
    {
        _videoGenerationPort = videoGenerationPort ?? throw new ArgumentNullException(nameof(videoGenerationPort));
        _objectStoragePort = objectStoragePort ?? throw new ArgumentNullException(nameof(objectStoragePort));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _generationAttemptRepository = generationAttemptRepository ?? throw new ArgumentNullException(nameof(generationAttemptRepository));
        _modelSelection = modelSelection ?? throw new ArgumentNullException(nameof(modelSelection));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.IsVideoRun)
        {
            _logger.LogInformation(
                "Skipping video generation: this execution is not a video run (ExecutionId={ExecutionId})",
                context.ExecutionId);

            return StepResult.Skipped();
        }

        var text = context.Text
            ?? throw new InvalidOperationException("VideoGenerationStep requires a Text context.");

        _logger.LogInformation(
            "Starting video generation for PostId={PostId}, theme={Theme}",
            text.PostId,
            text.Theme);

        var chain = await _modelSelection.GetVideoChainAsync(cancellationToken);

        if (chain.Count == 0)
        {
            return StepResult.Failure(
                FailureStep.VideoGeneration,
                "No video models available (empty chain and no configured Video.ModelId)",
                "NO_MODELS_AVAILABLE");
        }

        var durationSeconds = _config.Video.GetEffectiveDurationSeconds();

        VideoGenerationResult? generated = null;
        var attemptNumber = 0;
        decimal actualCost = 0;

        foreach (var candidate in chain)
        {
            var estimatedCost = ModelCostEstimator.EstimateVideoCostPerRequest(candidate, durationSeconds);

            if (!ModelCostEstimator.FitsBudget(
                    context.AccumulatedCostUsd,
                    estimatedCost,
                    context.CostCeilingUsd))
            {
                _logger.LogError(
                    "Video generation budget exceeded: accumulated={Accumulated} + estimated={Estimated} > max={Max}",
                    context.AccumulatedCostUsd,
                    estimatedCost,
                    context.CostCeilingUsd);

                return StepResult.Failure(
                    FailureStep.VideoGeneration,
                    $"Video generation budget exceeded: {context.AccumulatedCostUsd} + {estimatedCost} > {context.CostCeilingUsd} USD",
                    "BUDGET_EXCEEDED");
            }

            attemptNumber++;

            var attemptStopwatch = Stopwatch.StartNew();

            try
            {
                generated = await _videoGenerationPort.GenerateVideoAsync(
                    text.Theme,
                    candidate.Id,
                    cancellationToken);
            }
            catch (Exception ex) when (IsModelLevelFailure(ex, cancellationToken))
            {
                attemptStopwatch.Stop();

                _logger.LogWarning(
                    ex,
                    "Video model {ModelId} failed on attempt {Attempt}: advancing to next candidate",
                    candidate.Id,
                    attemptNumber);

                await RecordAttemptAsync(
                    text.PostId,
                    attemptNumber,
                    candidate.Id,
                    AttemptStatus.Error,
                    Truncate(ex.Message, 255),
                    costUsd: null,
                    attemptStopwatch.ElapsedMilliseconds,
                    cancellationToken);

                continue;
            }

            attemptStopwatch.Stop();

            actualCost = generated.CostUsd ?? estimatedCost;
            context.AccumulatedCostUsd += actualCost;

            await RecordAttemptAsync(
                text.PostId,
                attemptNumber,
                candidate.Id,
                AttemptStatus.Success,
                rejectionReason: null,
                actualCost,
                attemptStopwatch.ElapsedMilliseconds,
                cancellationToken);

            _logger.LogInformation(
                "Video generated: PostId={PostId}, ModelId={ModelId}, SizeBytes={SizeBytes}, DurationSeconds={DurationSeconds}, CostUsd={CostUsd}",
                text.PostId,
                candidate.Id,
                generated.VideoBytes.Length,
                generated.DurationSeconds,
                actualCost);

            break;
        }

        if (generated is null)
        {
            return StepResult.Failure(
                FailureStep.VideoGeneration,
                $"All {chain.Count} video model candidate(s) failed",
                "ALL_MODELS_FAILED");
        }

        try
        {
            var videoBytes = generated.VideoBytes;

            var currentUsage = await _objectStoragePort.GetBucketUsageBytesAsync(cancellationToken);
            var quotaBytes = StorageConstants.MinioDefaultQuotaBytes;
            var newTotal = currentUsage + videoBytes.Length;

            if (newTotal > quotaBytes)
            {
                _logger.LogError(
                    "MinIO quota exceeded: current={CurrentBytes}, new={NewTotal}, quota={QuotaBytes}",
                    currentUsage,
                    newTotal,
                    quotaBytes);

                return StepResult.Failure(
                    FailureStep.VideoStorage,
                    $"MinIO quota exceeded: {newTotal} bytes would exceed {quotaBytes} bytes limit",
                    "QUOTA_EXCEEDED");
            }

            var objectKey = Guid.NewGuid().ToString("N");
            await _objectStoragePort.PutObjectAsync(
                objectKey,
                videoBytes,
                "video/mp4",
                cancellationToken);

            _logger.LogInformation(
                "Video uploaded to MinIO: key={ObjectKey}, size={SizeBytes}",
                objectKey,
                videoBytes.Length);

            var post = await _postRepository.GetByIdAsync(text.PostId, cancellationToken);
            if (post is null)
            {
                _logger.LogError("Post {PostId} not found when updating video metadata", text.PostId);
                return StepResult.Failure(
                    FailureStep.VideoStorage,
                    $"Post {text.PostId} not found",
                    "POST_NOT_FOUND");
            }

            post.VideoObjectKey = objectKey;
            post.VideoBytes = videoBytes.Length;
            post.VideoDurationSeconds = generated.DurationSeconds;
            // PostStatus.ImageProcessed is reused as "media processed" for video (RF-17,
            // decision 8.2.5): no enum migration in the MVP.
            post.Status = PostStatus.ImageProcessed;
            post.UpdatedAt = DateTime.UtcNow;

            await _postRepository.UpdateAsync(post, cancellationToken);

            context.Video = new VideoContext(
                ObjectKey: objectKey,
                Bytes: videoBytes.Length,
                DurationSeconds: generated.DurationSeconds,
                CostUsd: actualCost,
                ModelId: generated.ModelId);

            _logger.LogInformation(
                "Video generation completed successfully: PostId={PostId}, key={ObjectKey}",
                text.PostId,
                objectKey);

            return StepResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: propagate so the orchestrator can stop the run cleanly.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Video storage failed for PostId={PostId}",
                text.PostId);

            return StepResult.Failure(
                FailureStep.VideoGeneration,
                $"Video generation failed: {ex.Message}",
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
                "Failed to record video generation attempt {Attempt} for model {ModelId}",
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
