using OddOddities.Application.Abstractions;
using OddOddities.Application.Pipeline;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Worker;

/// <summary>
/// Background service that runs the content generation and publishing pipeline.
/// Uses PeriodicTimer / delay for scheduling and IPipelineRunner for thread-safe concurrency control.
/// Implements RF-02: Agendamento de execucoes.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ISchedulerPort _scheduler;
    private readonly IPipelineRunner _pipelineRunner;
    private readonly IClock _clock;

    public Worker(
        ILogger<Worker> logger,
        ISchedulerPort scheduler,
        IPipelineRunner pipelineRunner,
        IClock clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _pipelineRunner = pipelineRunner ?? throw new ArgumentNullException(nameof(pipelineRunner));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting at {Time}", _clock.UtcNow);

        if (_scheduler.ShouldRunNow())
        {
            await _pipelineRunner.RunAsync(stoppingToken);
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

            await _pipelineRunner.RunAsync(stoppingToken);
        }

        _logger.LogInformation("Worker stopping at {Time}", _clock.UtcNow);
    }
}
