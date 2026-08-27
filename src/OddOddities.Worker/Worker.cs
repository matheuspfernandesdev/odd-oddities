using Microsoft.Extensions.Options;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Worker;

/// <summary>
/// Background service that runs the content generation and publishing pipeline.
/// Uses PeriodicTimer for scheduling and SemaphoreSlim for concurrency control.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AppConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Worker(
        ILogger<Worker> logger,
        IOptions<AppConfiguration> configuration,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting at {Time}", DateTimeOffset.UtcNow);

        var scheduleConfig = _configuration.Schedule;
        var days = ParseDays(scheduleConfig.Days);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24), TimeProvider.System);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            var localTime = now.AddHours(-5); // Simplified UTC to ET conversion

            if (ShouldRunToday(localTime, days) && localTime.Hour == scheduleConfig.HourUtc)
            {
                if (!await _lock.WaitAsync(0, stoppingToken))
                {
                    _logger.LogWarning("Pipeline already running, skipping this tick");
                    continue;
                }

                try
                {
                    await RunPipelineAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipeline execution failed");
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        _logger.LogInformation("Worker stopping at {Time}", DateTimeOffset.UtcNow);
    }

    private async Task RunPipelineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting pipeline execution at {Time}", DateTimeOffset.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        // TODO: Resolve pipeline service and execute
        // var pipeline = scope.ServiceProvider.GetRequiredService<IPipelineService>();
        // await pipeline.ExecuteAsync(cancellationToken);

        _logger.LogInformation("Pipeline execution completed at {Time}", DateTimeOffset.UtcNow);
    }

    private static bool ShouldRunToday(DateTime localTime, HashSet<DayOfWeek> days)
    {
        return days.Contains(localTime.DayOfWeek);
    }

    private static HashSet<DayOfWeek> ParseDays(string daysConfig)
    {
        var result = new HashSet<DayOfWeek>();
        var dayMap = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUN"] = DayOfWeek.Sunday,
            ["MON"] = DayOfWeek.Monday,
            ["TUE"] = DayOfWeek.Tuesday,
            ["WED"] = DayOfWeek.Wednesday,
            ["THU"] = DayOfWeek.Thursday,
            ["FRI"] = DayOfWeek.Friday,
            ["SAT"] = DayOfWeek.Saturday
        };

        foreach (var day in daysConfig.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (dayMap.TryGetValue(day.Trim(), out var dayOfWeek))
            {
                result.Add(dayOfWeek);
            }
        }

        return result;
    }
}
