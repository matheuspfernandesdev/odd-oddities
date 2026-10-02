namespace OddOddities.Application.Pipeline;

/// <summary>
/// Abstraction for executing the pipeline with thread-safe concurrency control.
/// </summary>
public interface IPipelineRunner
{
    Task<PipelineRunResult> RunAsync(CancellationToken cancellationToken = default);
}
