using OddOddities.Domain.ValueObjects;

namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for asynchronous video generation via OpenRouter (RF-17).
/// The model is resolved by the caller (preferred model + dynamic fallback chain).
/// A single call submits a job, polls it until it finishes and downloads the resulting
/// MP4, so it can take minutes. Failures of any phase are reported as
/// <see cref="Exceptions.OpenRouterModelException"/> (model-level, advances the fallback
/// chain); caller cancellation always propagates as <see cref="OperationCanceledException"/>.
/// </summary>
public interface IVideoGenerationPort
{
    Task<VideoGenerationResult> GenerateVideoAsync(
        string prompt,
        string modelId,
        CancellationToken cancellationToken = default);
}
