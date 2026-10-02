namespace OddOddities.Application.Pipeline;

/// <summary>
/// Represents the result of a pipeline execution.
/// </summary>
public sealed record PipelineExecutionResult(
    bool IsSuccess,
    string ExecutionId,
    string? FailureReason = null)
{
    public static PipelineExecutionResult Success(string executionId) =>
        new(true, executionId);

    public static PipelineExecutionResult Failed(string executionId, string failureReason) =>
        new(false, executionId, failureReason);
}
