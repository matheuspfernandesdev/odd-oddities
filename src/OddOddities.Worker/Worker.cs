using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Application.Abstractions;
using OddOddities.Application.Pipeline;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

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
    private readonly IClock _clock;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Worker(
        ILogger<Worker> logger,
        ISchedulerPort scheduler,
        IServiceScopeFactory scopeFactory,
        IClock clock)
    {
        _logger = logger;
        _scheduler = scheduler;
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting at {Time}", _clock.UtcNow);

        if (_scheduler.ShouldRunNow())
        {
            await TryExecutePipelineAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = _scheduler.GetNextRunTime();
            var delay = nextRun - _clock.UtcNow;

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
                    break;
                }
            }

            await TryExecutePipelineAsync(stoppingToken);
        }

        _logger.LogInformation("Worker stopping at {Time}", _clock.UtcNow);
    }

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

    private async Task RunPipelineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting pipeline execution at {Time}", _clock.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<PipelineOrchestrator>();
        await pipeline.ExecuteAsync(cancellationToken);

        _logger.LogInformation("Pipeline execution completed at {Time}", _clock.UtcNow);
    }
}
