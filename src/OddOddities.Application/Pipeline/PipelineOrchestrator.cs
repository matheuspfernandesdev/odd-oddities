using Microsoft.Extensions.Logging;
using OddOddities.Application.Ports;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Pipeline;

/// <summary>
/// Orchestrates the content pipeline (RF-01) with step-level error handling (RF-11).
/// Executes each IPipelineStep in sequence, tracking executionId, step, and outcome
/// via structured logging. On failure, marks the Post as Failed with the appropriate FailureStep.
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IEnumerable<IPipelineStep> _steps;
    private readonly ICategorySelectionPort _categorySelectionPort;
    private readonly IPostRepository _postRepository;
    private readonly ILogCorrelationPort _logCorrelation;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IEnumerable<IPipelineStep> steps,
        ICategorySelectionPort categorySelectionPort,
        IPostRepository postRepository,
        ILogCorrelationPort logCorrelation,
        ILogger<PipelineOrchestrator> logger)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _categorySelectionPort = categorySelectionPort ?? throw new ArgumentNullException(nameof(categorySelectionPort));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _logCorrelation = logCorrelation ?? throw new ArgumentNullException(nameof(logCorrelation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the full pipeline: category selection, text generation, validation,
    /// image generation, upload, and publishing.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var executionId = Guid.NewGuid().ToString("N");

        _logger.LogInformation("Pipeline started for execution {ExecutionId}", executionId);

        var (selectedCategory, selectedSubcategory) = await _categorySelectionPort
            .SelectBalancedCategoryAsync(cancellationToken);

        var context = PipelineContext.Create(
            executionId,
            selectedCategory,
            selectedSubcategory);

        _logger.LogInformation(
            "Pipeline selected category {Category}/{Subcategory}",
            selectedCategory.Name,
            selectedSubcategory.Name);

        foreach (var step in _steps)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using (_logCorrelation.PushCorrelation(executionId, step.StepName, "InProgress", 0))
            {
                _logger.LogInformation("Executing step {Step}", step.StepName);
            }

            try
            {
                var result = await step.ExecuteAsync(context, cancellationToken);
                stopwatch.Stop();

                if (result.IsSuccess)
                {
                    using (_logCorrelation.PushCorrelation(executionId, step.StepName, "Success", stopwatch.ElapsedMilliseconds))
                    {
                        _logger.LogInformation(
                            "Step {Step} completed in {Duration}ms",
                            step.StepName,
                            stopwatch.ElapsedMilliseconds);
                    }
                }
                else
                {
                    using (_logCorrelation.PushCorrelation(executionId, step.StepName, "Failed", stopwatch.ElapsedMilliseconds))
                    {
                        _logger.LogError(
                            "Step {Step} failed: {FailureReason} (FailureStep={FailureStep})",
                            step.StepName,
                            result.FailureReason,
                            result.FailureStepName);
                    }

                    await MarkPostAsFailedAsync(
                        context.Text?.PostId ?? 0,
                        result.FailureStep ?? FailureStepMap.FromStepName(step.StepName),
                        result.FailureReason ?? "Unknown failure",
                        result.ErrorCode,
                        cancellationToken);

                    return;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                using (_logCorrelation.PushCorrelation(executionId, step.StepName, "Failed", stopwatch.ElapsedMilliseconds))
                {
                    _logger.LogError(ex,
                        "Unhandled exception in step {Step}: {ExceptionType}",
                        step.StepName,
                        ex.GetType().Name);
                }

                var failureStep = FailureStepMap.FromStepName(step.StepName);

                await MarkPostAsFailedAsync(
                    context.Text?.PostId ?? 0,
                    failureStep,
                    $"Unexpected error in {step.StepName}: {ex.Message}",
                    ex.GetType().Name,
                    cancellationToken);

                return;
            }
        }

        _logger.LogInformation("Pipeline completed successfully for execution {ExecutionId}", executionId);
    }

    private async Task MarkPostAsFailedAsync(
        long postId,
        FailureStep failureStep,
        string failureReason,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        if (postId <= 0)
        {
            _logger.LogWarning("Cannot mark post as Failed: postId is {PostId}", postId);
            return;
        }

        var post = await _postRepository.GetByIdAsync(postId, cancellationToken);
        if (post is null)
        {
            _logger.LogWarning("Post {PostId} not found when marking as Failed", postId);
            return;
        }

        post.Status = PostStatus.Failed;
        post.FailureStep = failureStep;
        post.FailureReason = failureReason;
        post.ErrorCode = errorCode;
        post.FailureDetails = failureReason;
        post.UpdatedAt = DateTime.UtcNow;

        await _postRepository.UpdateAsync(post, cancellationToken);

        _logger.LogWarning(
            "Post {PostId} marked as Failed (step={FailureStep}, reason={FailureReason})",
            postId,
            failureStep,
            failureReason);
    }
}
