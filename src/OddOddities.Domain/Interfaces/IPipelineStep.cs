namespace OddOddities.Domain.Interfaces;

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
        PipelineExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a single pipeline step execution.
/// </summary>
public sealed class StepResult
{
    public bool IsSuccess { get; }
    public string? FailureStep { get; }
    public string? FailureReason { get; }
    public string? ErrorCode { get; }

    private StepResult(bool isSuccess, string? failureStep, string? failureReason, string? errorCode)
    {
        IsSuccess = isSuccess;
        FailureStep = failureStep;
        FailureReason = failureReason;
        ErrorCode = errorCode;
    }

    public static StepResult Success() => new(true, null, null, null);

    public static StepResult Failure(string failureStep, string failureReason, string? errorCode = null)
        => new(false, failureStep, failureReason, errorCode);
}

/// <summary>
/// Shared context passed through pipeline steps, carrying state between steps.
/// </summary>
public sealed class PipelineExecutionContext
{
    public long PostId { get; set; }
    public string ExecutionId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string SubcategoryName { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public long SubcategoryId { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public byte[]? ImageBytes { get; set; }
    public string ImageObjectKey { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string MetaMediaId { get; set; } = string.Empty;
    public string MetaPermalink { get; set; } = string.Empty;
}
