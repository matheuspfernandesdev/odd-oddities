using OddOddities.Application.Services;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Worker;

/// <summary>
/// Background service that runs the content generation and publishing pipeline.
/// Uses PeriodicTimer for scheduling and SemaphoreSlim for concurrency control.
/// Implements RF-02: Agendamento de execucoes.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ISchedulerPort _scheduler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Worker(
        ILogger<Worker> logger,
        ISchedulerPort scheduler,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scheduler = scheduler;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting at {Time}", DateTimeOffset.UtcNow);

        // Check immediately if we should run now (e.g., after restart during scheduled time)
        if (_scheduler.ShouldRunNow())
        {
            await TryExecutePipelineAsync(stoppingToken);
        }

        // Calculate delay to next scheduled run
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = _scheduler.GetNextRunTime();
            var delay = nextRun - DateTime.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Next pipeline run scheduled for {NextRun} UTC (in {Delay})",
                    nextRun,
                    delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown
                    break;
                }
            }

            // Execute pipeline if we should run now
            await TryExecutePipelineAsync(stoppingToken);
        }

        _logger.LogInformation("Worker stopping at {Time}", DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Attempts to execute the pipeline, respecting the semaphore to prevent parallel execution.
    /// </summary>
    private async Task TryExecutePipelineAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Pipeline already running, skipping this execution");
            return;
        }

        try
        {
            await RunPipelineAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Runs the pipeline by creating a scope and resolving the PipelineOrchestrator.
    /// </summary>
    private async Task RunPipelineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting pipeline execution at {Time}", DateTimeOffset.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<PipelineOrchestrator>();

        // Category selection will be done by the first pipeline step
        // For now, execute with placeholder values - will be wired up when RF-06 is implemented
        await pipeline.ExecuteAsync(
            categoryId: 0,
            subcategoryId: 0,
            categoryName: string.Empty,
            subcategoryName: string.Empty,
            cancellationToken);

        _logger.LogInformation("Pipeline execution completed at {Time}", DateTimeOffset.UtcNow);
    }
}
