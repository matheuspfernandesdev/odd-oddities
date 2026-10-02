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
    /// <returns>A result indicating success or the failure reason.</returns>
    Task<StepResult> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a single pipeline step execution.
/// </summary>
public sealed class StepResult
{
    public bool IsSuccess { get; }
    public FailureStep? FailureStep { get; }
    public string? FailureReason { get; }
    public string? ErrorCode { get; }

    /// <summary>
    /// String form of the failure step for log fields. Preserves the previous string-based
    /// logging contract (e.g. "TextGeneration", "InstagramApi") without runtime parsing.
    /// </summary>
    public string? FailureStepName => FailureStep?.ToString();

    private StepResult(bool isSuccess, FailureStep? failureStep, string? failureReason, string? errorCode)
    {
        IsSuccess = isSuccess;
        FailureStep = failureStep;
        FailureReason = failureReason;
        ErrorCode = errorCode;
    }

    public static StepResult Success() => new(true, null, null, null);

    public static StepResult Failure(FailureStep failureStep, string failureReason, string? errorCode = null)
        => new(false, failureStep, failureReason, errorCode);
}
