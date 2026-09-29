using OddOddities.Domain.Enums;

namespace OddOddities.Application.Pipeline;

/// <summary>
/// Defines the contract for a single step in the content pipeline.
/// Each step encapsulates a discrete unit of work with its own error handling semantics.
/// </summary>
public interface IPipelineStep
{
    /// <summary>
    /// Gets the name of this pipeline step, used for logging and FailureStep mapping.
    /// </summary>
    string StepName { get; }

    /// <summary>
    /// Executes this pipeline step within the given context.
    /// </summary>
    /// <param name="context">The shared pipeline execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A result indicating success, skip (step not applicable to this execution) or the failure reason.
    /// </returns>
    Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a single pipeline step execution (RF-13).
/// </summary>
public enum StepOutcome
{
    /// <summary>
    /// The step ran and completed its work. The pipeline continues.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The step does not apply to this execution (e.g. image step on a video run).
    /// The pipeline continues to the next step and the Post is never marked as Failed.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// The step failed. The pipeline stops and the Post is marked as Failed.
    /// </summary>
    Failed = 2
}

/// <summary>
/// Represents the result of a single pipeline step execution.
/// </summary>
public sealed class StepResult
{
    /// <summary>
    /// Gets the outcome of this step: Success, Skipped or Failed.
    /// </summary>
    public StepOutcome Outcome { get; }

    /// <summary>
    /// Gets whether the step did not fail. Both <see cref="StepOutcome.Success"/> and
    /// <see cref="StepOutcome.Skipped"/> are considered successful (a skipped step is
    /// an expected decision, not an error).
    /// </summary>
    public bool IsSuccess => Outcome != StepOutcome.Failed;

    public FailureStep? FailureStep { get; }
    public string? FailureReason { get; }
    public string? ErrorCode { get; }

    /// <summary>
    /// String form of the failure step for log fields. Preserves the previous string-based
    /// logging contract (e.g. "TextGeneration", "InstagramApi") without runtime parsing.
    /// </summary>
    public string? FailureStepName => FailureStep?.ToString();

    private StepResult(StepOutcome outcome, FailureStep? failureStep, string? failureReason, string? errorCode)
    {
        Outcome = outcome;
        FailureStep = failureStep;
        FailureReason = failureReason;
        ErrorCode = errorCode;
    }

    public static StepResult Success() => new(StepOutcome.Success, null, null, null);

    /// <summary>
    /// Creates a result for a step that does not apply to the current execution.
    /// <see cref="IsSuccess"/> is <c>true</c> and <see cref="FailureStep"/> is <c>null</c>,
    /// so the orchestrator continues without ever marking the Post as Failed (RF-13).
    /// </summary>
    public static StepResult Skipped() => new(StepOutcome.Skipped, null, null, null);

    public static StepResult Failure(FailureStep failureStep, string failureReason, string? errorCode = null)
        => new(StepOutcome.Failed, failureStep, failureReason, errorCode);
}
