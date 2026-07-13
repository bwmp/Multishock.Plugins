using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

/// <summary>
/// Interprets an OpenCV MatchTemplate result map according to the match method:
/// SqDiff variants are distance metrics (lower = better, match at minLoc), the
/// others are similarity metrics (higher = better, match at maxLoc).
/// </summary>
internal static class TemplateMatchInterpreter
{
    public static (double Confidence, System.Drawing.Point Location) GetBestMatch(
        Mat result, TemplateMatchingType method)
    {
        double minVal = 0, maxVal = 0;
        System.Drawing.Point minLoc = default, maxLoc = default;
        CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

        return method switch
        {
            TemplateMatchingType.SqdiffNormed => (1.0 - minVal, minLoc),
            // Raw Sqdiff is unbounded; map to (0,1] so thresholds stay comparable.
            TemplateMatchingType.Sqdiff => (1.0 / (1.0 + minVal), minLoc),
            _ => (maxVal, maxLoc)
        };
    }
}

public class TemplateMatchingAlgorithm : IDetectionAlgorithm
{
    public string Id => "template-matching";
    public string Name => "Template Matching";
    public string Description => "Fast exact image matching using OpenCV. Best for UI elements, icons, and static images that don't change size or rotation.";
    public bool SupportsFuzzyMatching => false;

    public TemplateMatchingType MatchMethod { get; set; } = TemplateMatchingType.CcoeffNormed;

    public DetectionResult Detect(
        Mat screenshot,
        Mat template,
        double threshold,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (screenshot == null || screenshot.IsEmpty)
            {
                return DetectionResult.Failed("Screenshot is null or empty");
            }

            if (template == null || template.IsEmpty)
            {
                return DetectionResult.Failed("Template image is null or empty");
            }

            if (template.Width > screenshot.Width || template.Height > screenshot.Height)
            {
                return DetectionResult.Failed(
                    $"Template ({template.Width}x{template.Height}) is larger than screenshot ({screenshot.Width}x{screenshot.Height})");
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var result = new Mat();
            if (screenshot.NumberOfChannels != template.NumberOfChannels)
                return DetectionResult.Failed("Screenshot and template channel counts do not match");

            CvInvoke.MatchTemplate(screenshot, template, result, MatchMethod);

            cancellationToken.ThrowIfCancellationRequested();

            var (confidence, bestLoc) = TemplateMatchInterpreter.GetBestMatch(result, MatchMethod);

            stopwatch.Stop();

            var matchLocation = new Models.Point(bestLoc.X, bestLoc.Y);
            var matchSize = new Models.Size(template.Width, template.Height);

            if (confidence >= threshold)
            {
                return new DetectionResult
                {
                    Found = true,
                    Confidence = confidence,
                    Threshold = threshold,
                    MatchLocation = matchLocation,
                    MatchSize = matchSize,
                    AlgorithmId = Id,
                    DetectionTime = stopwatch.Elapsed
                };
            }

            return new DetectionResult
            {
                Found = false,
                Confidence = confidence,
                Threshold = threshold,
                AlgorithmId = Id,
                DetectionTime = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return DetectionResult.Failed("Detection was cancelled");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return DetectionResult.Failed($"Template matching failed: {ex.Message}");
        }
    }

    public bool IsAvailable() => true;

    public string? GetUnavailableReason() => null;
}


public class MaskedTemplateMatchingAlgorithm : IDetectionAlgorithm
{
    public string Id => "template-matching-masked";
    public string Name => "Template Matching (Masked)";
    public string Description => "Template matching that respects transparent regions in the template image. Use for images with transparency.";
    public bool SupportsFuzzyMatching => false;

    public TemplateMatchingType MatchMethod { get; set; } = TemplateMatchingType.CcoeffNormed;

    public DetectionResult Detect(
        Mat screenshot,
        Mat template,
        double threshold,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (screenshot == null || screenshot.IsEmpty)
                return DetectionResult.Failed("Screenshot is null or empty");

            if (template == null || template.IsEmpty)
                return DetectionResult.Failed("Template image is null or empty");

            if (template.Width > screenshot.Width || template.Height > screenshot.Height)
                return DetectionResult.Failed("Template is larger than screenshot");

            cancellationToken.ThrowIfCancellationRequested();

            Mat? mask = null;
            Mat templateBgr;

            if (template.NumberOfChannels == 4)
            {

                var channels = template.Split();
                mask = channels[3]; // Alpha channel

                CvInvoke.Threshold(mask, mask, 100, 255, ThresholdType.Binary);

                templateBgr = new Mat();
                CvInvoke.CvtColor(template, templateBgr, ColorConversion.Bgra2Bgr);

                // Dispose other channels
                channels[0].Dispose();
                channels[1].Dispose();
                channels[2].Dispose();
            }
            else
            {
                templateBgr = template.NumberOfChannels == 1
                    ? new Mat()
                    : template.Clone();

                if (template.NumberOfChannels == 1)
                    CvInvoke.CvtColor(template, templateBgr, ColorConversion.Gray2Bgr);
            }

            using var screenshotBgr = screenshot.NumberOfChannels == 4
                ? new Mat()
                : (screenshot.NumberOfChannels == 1 ? new Mat() : screenshot.Clone());

            if (screenshot.NumberOfChannels == 4)
                CvInvoke.CvtColor(screenshot, screenshotBgr, ColorConversion.Bgra2Bgr);
            else if (screenshot.NumberOfChannels == 1)
                CvInvoke.CvtColor(screenshot, screenshotBgr, ColorConversion.Gray2Bgr);

            using var result = new Mat();

            if (mask != null)
            {
                CvInvoke.MatchTemplate(screenshotBgr, templateBgr, result, MatchMethod, mask);
                mask.Dispose();
            }
            else
            {
                CvInvoke.MatchTemplate(screenshotBgr, templateBgr, result, MatchMethod);
            }

            templateBgr.Dispose();

            cancellationToken.ThrowIfCancellationRequested();

            var (confidence, bestLoc) = TemplateMatchInterpreter.GetBestMatch(result, MatchMethod);

            stopwatch.Stop();

            var matchLocation = new Models.Point(bestLoc.X, bestLoc.Y);
            var matchSize = new Models.Size(template.Width, template.Height);

            if (confidence >= threshold)
            {
                return new DetectionResult
                {
                    Found = true,
                    Confidence = confidence,
                    Threshold = threshold,
                    MatchLocation = matchLocation,
                    MatchSize = matchSize,
                    AlgorithmId = Id,
                    DetectionTime = stopwatch.Elapsed
                };
            }

            return new DetectionResult
            {
                Found = false,
                Confidence = confidence,
                Threshold = threshold,
                AlgorithmId = Id,
                DetectionTime = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return DetectionResult.Failed("Detection was cancelled");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return DetectionResult.Failed($"Masked template matching failed: {ex.Message}");
        }
    }

    public bool IsAvailable() => true;
    public string? GetUnavailableReason() => null;
}
