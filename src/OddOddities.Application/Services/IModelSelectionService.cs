using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Builds the ordered model candidate chain used by the text/image/video pipeline steps:
/// preferred model from config first, then dynamic candidates from the OpenRouter
/// catalog (free first, then cheapest within the configured cost ceiling).
/// The catalog is fetched lazily once per DI scope (= once per pipeline execution).
/// On catalog failure it falls back to the preferred model only, so the pipeline
/// keeps working exactly as before.
/// </summary>
public interface IModelSelectionService
{
    Task<IReadOnlyList<ModelDescriptor>> GetTextChainAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelDescriptor>> GetImageChainAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Video chain: preferred <c>AppConfiguration.Video.ModelId</c> first (even when
    /// missing from the catalog), then free candidates, then the cheapest per second.
    /// Candidates are filtered by supported aspect ratio (<c>Video.AspectRatio</c>),
    /// supported duration (<c>Video.DurationSeconds</c>) and the estimated per-request
    /// cost (cost/second x duration &lt;= MaxVideoCostPerRequestUsd), capped at
    /// MaxVideoModelAttempts models.
    /// </summary>
    Task<IReadOnlyList<VideoModelDescriptor>> GetVideoChainAsync(CancellationToken cancellationToken = default);
}
