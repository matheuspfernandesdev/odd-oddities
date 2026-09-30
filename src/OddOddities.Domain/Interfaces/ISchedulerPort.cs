namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for schedule calculations. Responsible for determining the next run time
/// based on configured days, hour, and timezone (with DST support).
/// </summary>
public interface ISchedulerPort
{
    /// <summary>
    /// Gets the next UTC time when the pipeline should execute,
    /// based on configured schedule (days, hour, timezone).
    /// </summary>
    /// <returns>The next run time in UTC.</returns>
    DateTime GetNextRunTime();

    /// <summary>
    /// Checks if the pipeline should run right now, based on the configured schedule.
    /// Uses the current UTC time, converts to the target timezone, and checks
    /// if today is a configured day and if the current hour matches the configured hour.
    /// </summary>
    /// <returns>True if the pipeline should run now; otherwise, false.</returns>
    bool ShouldRunNow();

    /// <summary>
    /// Checks if the current pipeline execution must generate a video instead of an
    /// image (RF-15). The rule compares today's date against the PublishedAt date of the
    /// latest published Post that has a video (VideoObjectKey != null) using
    /// AppConfiguration.Video.IntervalDays. When no video was ever published the answer
    /// is true; when the underlying lookup fails the answer is false (image run, the
    /// behavior that existed before videos were introduced).
    /// </summary>
    /// <returns>True if this execution is a video run; otherwise, false.</returns>
    bool IsVideoRunToday();
}
