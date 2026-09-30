using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Application.Ports;
using OddOddities.Domain.Enums;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Pipeline;

/// <summary>
/// Orchestrates the content pipeline (RF-01) with step-level error handling (RF-11).
/// Executes each IPipelineStep in sequence, tracking executionId, step, and outcome
/// via structured logging. On failure, marks the Post as Failed with the appropriate FailureStep.
/// Steps returning <see cref="StepOutcome.Skipped"/> are logged as "Skipped" and the pipeline
/// continues to the next step without touching the Post (RF-13).
/// The execution modality (image vs video) is decided once per run and exposed to the
/// steps through PipelineContext.IsVideoRun / PipelineContext.CostCeilingUsd (RF-15).
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IEnumerable<IPipelineStep> _steps;
    private readonly ICategorySelectionPort _categorySelectionPort;
    private readonly IPostRepository _postRepository;
    private readonly ISchedulerPort _scheduler;
    private readonly ILogCorrelationPort _logCorrelation;
    private readonly IOptions<AppConfiguration> _config;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IEnumerable<IPipelineStep> steps,
        ICategorySelectionPort categorySelectionPort,
        IPostRepository postRepository,
        ISchedulerPort scheduler,
        ILogCorrelationPort logCorrelation,
        IOptions<AppConfiguration> config,
        ILogger<PipelineOrchestrator> logger)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _categorySelectionPort = categorySelectionPort ?? throw new ArgumentNullException(nameof(categorySelectionPort));
        _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _logCorrelation = logCorrelation ?? throw new ArgumentNullException(nameof(logCorrelation));
        _config = config ?? throw new ArgumentNullException(nameof(config));
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

        ApplyModality(context);

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

                if (result.Outcome == StepOutcome.Skipped)
                {
                    // RF-13: a step that does not apply to this execution is skipped, never fails
                    // the pipeline and never marks the Post as Failed. The foreach continues.
                    using (_logCorrelation.PushCorrelation(executionId, step.StepName, "Skipped", stopwatch.ElapsedMilliseconds))
                    {
                        _logger.LogInformation(
                            "Step {Step} skipped after {Duration}ms - not applicable to this execution, continuing",
                            step.StepName,
                            stopwatch.ElapsedMilliseconds);
                    }
                }
                else if (result.IsSuccess)
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();

                _logger.LogWarning(
                    "Pipeline cancelled while executing step {Step} for execution {ExecutionId}",
                    step.StepName,
                    executionId);

                return;
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

    /// <summary>
    /// Decides the execution modality exactly once per run (RF-15) and writes both the
    /// decision and its effective cost ceiling to the context, so no step has to decide.
    /// </summary>
    private void ApplyModality(PipelineContext context)
    {
        context.IsVideoRun = _scheduler.IsVideoRunToday();

        var modelSelection = _config.Value.ModelSelection;

        context.CostCeilingUsd = context.IsVideoRun
            ? modelSelection.MaxCostPerVideoRunUsd
            : modelSelection.MaxCostPerRunUsd;

        _logger.LogInformation(
            "Execution modality decided: IsVideoRun={IsVideoRun}, CostCeilingUsd={CostCeilingUsd} for execution {ExecutionId}",
            context.IsVideoRun,
            context.CostCeilingUsd,
            context.ExecutionId);
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

        try
        {
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
        catch (Exception ex)
        {
            // Marking the post as Failed must never mask the original step failure.
            _logger.LogError(
                ex,
                "Failed to mark Post {PostId} as Failed (step={FailureStep})",
                postId,
                failureStep);
        }
    }
}
