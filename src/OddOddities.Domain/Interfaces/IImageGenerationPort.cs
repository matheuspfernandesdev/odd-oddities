namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for image generation via OpenRouter.
/// </summary>
public interface IImageGenerationPort
{
    Task<byte[]> GenerateImageAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}
