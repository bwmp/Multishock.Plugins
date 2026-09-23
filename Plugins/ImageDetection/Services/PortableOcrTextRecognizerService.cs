#if !WINDOWS
using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

public sealed class OcrTextRecognizerService(ILogger? logger = null)
{
    private readonly ILogger? _logger = logger;

    public bool IsAvailable => FindExecutable("tesseract") is not null;

    public string? UnavailableReason => IsAvailable
        ? null
        : "OCR requires the tesseract executable on macOS and Linux.";

    public async Task<string?> ReadTextAsync(
        Mat roi,
        OcrDetectionConfig config,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || roi is null || roi.IsEmpty)
        {
            return null;
        }

        var inputPath = Path.Combine(Path.GetTempPath(), $"multishock-ocr-{Guid.NewGuid():N}.png");
        try
        {
            using var preprocessed = PrepareForOcr(roi, config);
            CvInvoke.Imwrite(inputPath, preprocessed);
            var startInfo = new ProcessStartInfo
            {
                FileName = FindExecutable("tesseract")!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("stdout");
            startInfo.ArgumentList.Add("--psm");
            startInfo.ArgumentList.Add("6");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                _logger?.LogDebug("Tesseract exited with code {ExitCode}: {Error}", process.ExitCode, error.Trim());
                return null;
            }

            var text = string.Join(" ", output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return string.IsNullOrWhiteSpace(text) ? null : text;
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
        finally
        {
            try
            {
                File.Delete(inputPath);
            }
            catch (IOException)
            {
            }
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
        else
        {
            gray = source.Clone();
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

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }
}
#endif
