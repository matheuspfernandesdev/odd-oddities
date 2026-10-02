using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OddOddities.Application.Pipeline;

/// <summary>
/// Thread-safe runner for executing the pipeline using a SemaphoreSlim lock.
/// Ensures that scheduled and manual executions cannot run simultaneously.
/// </summary>
public sealed class PipelineRunner : IPipelineRunner, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public PipelineRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<PipelineRunner> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PipelineRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Pipeline execution requested, but pipeline is already running");
            return PipelineRunResult.AlreadyRunning();
        }

        try
        {
            _logger.LogInformation("Pipeline execution acquired lock");
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<PipelineOrchestrator>();
            var executionResult = await orchestrator.ExecuteAsync(cancellationToken);

            if (executionResult.IsSuccess)
            {
                _logger.LogInformation("Pipeline execution completed successfully (ExecutionId: {ExecutionId})", executionResult.ExecutionId);
                return PipelineRunResult.Success(executionResult.ExecutionId);
            }
            else
            {
                var reason = executionResult.FailureReason ?? "Pipeline execution failed";
                _logger.LogError("Pipeline execution failed (ExecutionId: {ExecutionId}): {Reason}", executionResult.ExecutionId, reason);
                return PipelineRunResult.Failed(executionResult.ExecutionId, reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during pipeline runner execution");
            return PipelineRunResult.Failed(null, ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
