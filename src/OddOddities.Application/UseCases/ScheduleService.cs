using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.UseCases;

/// <summary>
/// Implements schedule logic for pipeline execution (RF-02).
/// Converts UTC to the configured timezone (with DST support) and determines
/// the next run time based on configured days and hour.
/// </summary>
public sealed class ScheduleService : ISchedulerPort
{
    private readonly ScheduleConfiguration _config;
    private readonly TimeZoneInfo _timeZone;
    private readonly HashSet<DayOfWeek> _configuredDays;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(
        IOptions<AppConfiguration> configuration,
        ILogger<ScheduleService> logger)
    {
        _config = configuration.Value.Schedule;
        _logger = logger;

        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_config.Timezone);

        _configuredDays = ParseDays(_config.Days);

        _logger.LogInformation(
            "ScheduleService initialized: Hour={HourUtc}, Timezone={Timezone}, Days={Days}",
            _config.HourUtc,
            _config.Timezone,
            _config.Days);
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
