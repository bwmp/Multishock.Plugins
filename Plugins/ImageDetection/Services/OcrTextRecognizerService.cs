using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ImageDetection.Services;

/// <summary>
/// OCR extractor backed by the Windows OCR engine.
/// </summary>
public sealed class OcrTextRecognizerService(ILogger? logger = null)
{
    private readonly ILogger? _logger = logger;
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    public bool IsAvailable => _engine != null;

    public string? UnavailableReason => IsAvailable
        ? null
        : "Windows OCR engine is unavailable on this system.";

    /// <summary>
    /// Reads text from the provided ROI using OCR preprocessing settings.
    /// </summary>
    public async Task<string?> ReadTextAsync(Mat roi, OcrDetectionConfig config, CancellationToken cancellationToken = default)
    {
        if (_engine == null || roi == null || roi.IsEmpty)
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var preprocessed = PrepareForOcr(roi, config);
            using var softwareBitmap = ToSoftwareBitmap(preprocessed);

            var ocrResult = await _engine.RecognizeAsync(softwareBitmap).AsTask();

            cancellationToken.ThrowIfCancellationRequested();

            var text = string.Join(" ", ocrResult.Lines
                .Select(line => line.Text)
                .Where(line => !string.IsNullOrWhiteSpace(line)));

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "OCR read failed");
            return null;
        }
    }

    private static Mat PrepareForOcr(Mat source, OcrDetectionConfig config)
    {
        var gray = new Mat();
        if (source.NumberOfChannels == 4)
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgra2Gray);
        }
        else if (source.NumberOfChannels == 3)
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
        }
        else if (source.NumberOfChannels == 1)
        {
            gray = source.Clone();
        }
        else
        {
            CvInvoke.CvtColor(source, gray, ColorConversion.Bgr2Gray);
        }

        var scale = Math.Clamp(config.PreprocessScale, 1.0, 4.0);
        if (scale > 1.01)
        {
            var scaled = new Mat();
            CvInvoke.Resize(gray, scaled, new System.Drawing.Size(0, 0), scale, scale, Inter.Cubic);
            gray.Dispose();
            gray = scaled;
        }

        if (config.UseThresholding)
        {
            var thresholded = new Mat();
            CvInvoke.Threshold(gray, thresholded, 0, 255, ThresholdType.Binary | ThresholdType.Otsu);
            gray.Dispose();
            gray = thresholded;
        }

        if (config.InvertColors)
        {
            var inverted = new Mat();
            CvInvoke.BitwiseNot(gray, inverted);
            gray.Dispose();
            gray = inverted;
        }

        return gray;
    }

    private static SoftwareBitmap ToSoftwareBitmap(Mat gray)
    {
        var width = gray.Width;
        var height = gray.Height;
        var stride = (int)gray.Step;

        var source = new byte[stride * height];
        Marshal.Copy(gray.DataPointer, source, 0, source.Length);

        var packed = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(source, y * stride, packed, y * width, width);
        }

        return SoftwareBitmap.CreateCopyFromBuffer(
            packed.AsBuffer(),
            BitmapPixelFormat.Gray8,
            width,
            height);
    }
}
