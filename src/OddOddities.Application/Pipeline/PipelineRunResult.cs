namespace OddOddities.Application.Pipeline;

/// <summary>
/// Status of a pipeline run request.
/// </summary>
public enum PipelineRunStatus
{
    Success,
    AlreadyRunning,
    Failed
}

/// <summary>
/// Result returned by <see cref="IPipelineRunner"/>.
/// </summary>
public sealed record PipelineRunResult(
    PipelineRunStatus Status,
    string? ExecutionId = null,
    string? FailureReason = null)
{
    public static PipelineRunResult Success(string executionId) =>
        new(PipelineRunStatus.Success, executionId);

    public static PipelineRunResult AlreadyRunning() =>
        new(PipelineRunStatus.AlreadyRunning);

    public static PipelineRunResult Failed(string? executionId, string failureReason) =>
        new(PipelineRunStatus.Failed, executionId, failureReason);
}
