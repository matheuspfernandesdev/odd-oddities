using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Application.Services;

/// <summary>
/// Image processing service implementing RF-09 using SixLabors.ImageSharp.
/// Handles decoding, resizing (1080x1080 with center crop), watermarking, and JPEG encoding.
/// </summary>
public sealed class ImageSharpProcessingService : IImageProcessingPort
{
    private readonly ImageProcessingConfiguration _config;
    private readonly ILogger<ImageSharpProcessingService> _logger;

    public ImageSharpProcessingService(
        IOptions<ImageProcessingConfiguration> options,
        ILogger<ImageSharpProcessingService> logger)
    {
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ImageProcessingResult> ProcessImageAsync(
        byte[] imageData,
        CancellationToken cancellationToken = default)
    {
        if (imageData == null || imageData.Length == 0)
        {
            throw new ArgumentException("Image data cannot be null or empty.", nameof(imageData));
        }

        _logger.LogInformation(
            "Starting image processing: input size = {InputSize} bytes",
            imageData.Length);

        using var image = Image.Load<Rgba32>(imageData);

        _logger.LogDebug(
            "Image decoded: {Width}x{Height}",
            image.Width,
            image.Height);

        // 1. Resize to 1080x1080 with center crop (maintains aspect ratio)
        var targetSize = new Size(_config.Width, _config.Height);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = targetSize,
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        _logger.LogDebug(
            "Image resized to {Width}x{Height}",
            image.Width,
            image.Height);

        // 2. Add watermark
        AddWatermark(image);

        _logger.LogDebug("Watermark applied");

        // 3. Save as JPEG with configured quality
        using var outputStream = new MemoryStream();
        await image.SaveAsJpegAsync(
            outputStream,
            new JpegEncoder { Quality = _config.Quality },
            cancellationToken);

        var processedBytes = outputStream.ToArray();

        _logger.LogInformation(
            "Image processing completed: output size = {OutputSize} bytes, {Width}x{Height}, JPEG quality = {Quality}",
            processedBytes.Length,
            _config.Width,
            _config.Height,
            _config.Quality);

        return new ImageProcessingResult
        {
            ImageData = processedBytes,
            Width = _config.Width,
            Height = _config.Height,
            Format = "jpeg"
        };
    }

    /// <summary>
    /// Adds a semi-transparent white watermark text to the bottom-right corner of the image.
    /// </summary>
    private void AddWatermark(Image<Rgba32> image)
    {
        var font = SystemFonts.CreateFont("Arial", _config.WatermarkFontSize, FontStyle.Regular);

        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(
                image.Width - 20,
                image.Height - 30),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        // Semi-transparent white (alpha = 180/255 ≈ 70% opacity)
        var brush = Brushes.Solid(Color.White.WithAlpha(0.7f));

        image.Mutate(ctx => ctx.Paint(canvas =>
        {
            canvas.DrawText(
                textOptions,
                _config.WatermarkText,
                brush,
                pen: null);
        }));
    }
}
