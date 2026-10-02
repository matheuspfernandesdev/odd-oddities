namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for image processing operations (RF-09).
/// Handles decoding, resizing, watermarking, and JPEG encoding of images.
/// </summary>
public interface IImageProcessingPort
{
    /// <summary>
    /// Processes raw image data by resizing to configured dimensions, adding a watermark,
    /// and encoding as JPEG.
    /// </summary>
    /// <param name="imageData">Raw image bytes (PNG or JPEG from AI generation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The processed image result with bytes, dimensions, and format.</returns>
    Task<ImageProcessingResult> ProcessImageAsync(
        byte[] imageData,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of image processing operations.
/// </summary>
public sealed class ImageProcessingResult
{
    /// <summary>Processed image bytes in JPEG format.</summary>
    public byte[] ImageData { get; init; } = [];

    /// <summary>Final image width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Final image height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Image format identifier (e.g., "jpeg").</summary>
    public string Format { get; init; } = string.Empty;
}
