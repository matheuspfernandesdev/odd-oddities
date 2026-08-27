namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for time operations with timezone support.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
    DateTime ConvertToTimezone(string timezoneId);
}
