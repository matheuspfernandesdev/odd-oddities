namespace OddOddities.Application.Ports;

/// <summary>
/// Port for managing log correlation context (executionId, step, outcome, durationMs).
/// Each pipeline step pushes its correlation properties so they appear in all subsequent logs.
/// </summary>
public interface ILogCorrelationPort
{
    /// <summary>
    /// Pushes correlation properties onto the current log context.
    /// Properties remain active for the lifetime of the returned IDisposable.
    /// </summary>
    /// <param name="executionId">Unique identifier for the current pipeline execution.</param>
    /// <param name="step">Name of the current pipeline step (e.g., TextGeneration, ImageGeneration).</param>
    /// <param name="outcome">Result of the step (e.g., Success, Failed, Rejected).</param>
    /// <param name="durationMs">Duration of the step in milliseconds.</param>
    /// <returns>An IDisposable that removes the properties when disposed.</returns>
    IDisposable PushCorrelation(
        string executionId,
        string step,
        string outcome,
        long durationMs);
}
