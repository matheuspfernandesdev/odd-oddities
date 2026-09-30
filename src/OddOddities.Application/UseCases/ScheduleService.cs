using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.UseCases;

/// <summary>
/// Implements schedule logic for pipeline execution (RF-02).
/// Converts UTC to the configured timezone (with DST support) and determines
/// the next run time based on configured days and hour.
/// Also resolves the execution modality (image vs video, RF-15) through
/// ISchedulerPort.IsVideoRunToday().
/// Registered as a singleton (consumed by the Worker), so any repository access is
/// performed inside a short-lived DI scope created through IServiceScopeFactory.
/// </summary>
public sealed class ScheduleService : ISchedulerPort
{
    private readonly ScheduleConfiguration _config;
    private readonly VideoConfiguration _videoConfig;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeZoneInfo _timeZone;
    private readonly HashSet<DayOfWeek> _configuredDays;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(
        IOptions<AppConfiguration> configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduleService> logger)
    {
        _config = configuration.Value.Schedule;
        _videoConfig = configuration.Value.Video;
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger;

        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_config.Timezone);

        _configuredDays = ParseDays(_config.Days);

        _logger.LogInformation(
            "ScheduleService initialized: Hour={HourUtc}, Timezone={Timezone}, Days={Days}, VideoIntervalDays={VideoIntervalDays}",
            _config.HourUtc,
            _config.Timezone,
            _config.Days,
            _videoConfig.IntervalDays);
    }

    /// <inheritdoc />
    public DateTime GetNextRunTime()
    {
        var nowUtc = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);

        var candidateDate = localNow.Date;
        var candidateTime = new TimeOnly(_config.HourUtc, 0);
        var candidateDateTime = candidateDate.Add(candidateTime.ToTimeSpan());

        if (candidateDateTime <= localNow)
        {
            candidateDate = candidateDate.AddDays(1);
            candidateDateTime = candidateDate.Add(candidateTime.ToTimeSpan());
        }

        for (int i = 0; i < 7; i++)
        {
            if (_configuredDays.Contains(candidateDateTime.DayOfWeek))
            {
                var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(candidateDateTime, _timeZone);

                _logger.LogDebug(
                    "Next run time: {NextRunUtc} UTC (Local: {NextRunLocal} {Timezone})",
                    nextRunUtc,
                    candidateDateTime,
                    _timeZone.Id);

                return nextRunUtc;
            }

            candidateDate = candidateDate.AddDays(1);
            candidateDateTime = candidateDate.Add(candidateTime.ToTimeSpan());
        }

        _logger.LogWarning("Could not find next configured day within 7 days, defaulting to 24h from now");
        return nowUtc.AddHours(24);
    }

    /// <inheritdoc />
    public bool ShouldRunNow()
    {
        var nowUtc = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);

        var isCorrectDay = _configuredDays.Contains(localNow.DayOfWeek);
        var isCorrectHour = localNow.Hour == _config.HourUtc;

        _logger.LogDebug(
            "Schedule check: LocalTime={LocalTime}, Day={DayOfWeek} (configured={IsCorrectDay}), Hour={Hour} (configured={IsCorrectHour})",
            localNow,
            localNow.DayOfWeek,
            isCorrectDay,
            localNow.Hour,
            isCorrectHour);

        return isCorrectDay && isCorrectHour;
    }

    /// <inheritdoc />
    public bool IsVideoRunToday()
    {
        DateTime? lastVideoPublishedAt;

        try
        {
            // This service is a singleton (consumed by the Worker), so the scoped
            // repository must be resolved inside a short-lived DI scope.
            using var scope = _scopeFactory.CreateScope();
            var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();

            // The port contract is synchronous: the modality is decided once per run,
            // before any step executes, so blocking on this single lookup is acceptable.
            lastVideoPublishedAt = postRepository
                .GetLatestVideoPublishedAtAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            // Fallback (RF-15): a failed lookup keeps the current behavior — image run.
            _logger.LogError(
                ex,
                "Could not read the latest published video date; defaulting to an image run");
            return false;
        }

        if (lastVideoPublishedAt is null)
        {
            _logger.LogInformation(
                "Video run decided: no video was ever published (IntervalDays={IntervalDays})",
                _videoConfig.IntervalDays);

            return true;
        }

        // Whole-day comparison: the cadence "one video every N days" is measured in days,
        // so time-of-day jitter between runs does not shift the interval.
        var elapsed = DateTime.UtcNow.Date - lastVideoPublishedAt.Value.Date;
        var isVideoRun = elapsed >= TimeSpan.FromDays(_videoConfig.IntervalDays);

        _logger.LogInformation(
            "Video run decision: LastVideoPublishedAt={LastVideoPublishedAt}, ElapsedDays={ElapsedDays}, IntervalDays={IntervalDays}, IsVideoRun={IsVideoRun}",
            lastVideoPublishedAt,
            elapsed.TotalDays,
            _videoConfig.IntervalDays,
            isVideoRun);

        return isVideoRun;
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
