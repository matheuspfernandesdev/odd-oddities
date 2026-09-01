namespace OddOddities.Application.Abstractions;

/// <summary>
/// Abstraction for time operations, replacing direct DateTime.UtcNow usage.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
