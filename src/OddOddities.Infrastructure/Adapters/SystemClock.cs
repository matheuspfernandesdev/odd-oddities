namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// System clock implementation that uses the machine's UTC time.
/// </summary>
public sealed class SystemClock : OddOddities.Application.Abstractions.IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
