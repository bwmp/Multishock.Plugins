using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

public static class MultiScaleMatcher
{
    public static DetectionResult Detect(
        IDetectionAlgorithm algorithm,
        Mat screenshot,
        Mat template,
        DetectionImage imageConfig,
        CancellationToken ct)
    {
        var config = imageConfig.MultiScale;
        // The hybrid owns its scale pyramid; wrapping it in another scale sweep
        // repeats the work and expands its intended search range.
        if (algorithm is HybridDetectionAlgorithm || imageConfig.TargetType != DetectionTargetType.Template || !config.Enabled)
        {
            return algorithm.Detect(screenshot, template, imageConfig.Threshold, ct);
        }

        DetectionResult? best = null;

        foreach (var scale in BuildScaleLadder(config))
        {
            ct.ThrowIfCancellationRequested();

            DetectionResult result;

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                result = algorithm.Detect(screenshot, template, imageConfig.Threshold, ct);
            }
            else
            {
                int w = (int)Math.Round(template.Width * scale);
                int h = (int)Math.Round(template.Height * scale);
                if (w < 2 || h < 2 || w > screenshot.Width || h > screenshot.Height)
                {
                    continue;
                }

                using var scaledTemplate = new Mat();
                CvInvoke.Resize(template, scaledTemplate, new System.Drawing.Size(w, h),
                    0, 0, scale < 1.0 ? Inter.Area : Inter.Cubic);
                result = algorithm.Detect(screenshot, scaledTemplate, imageConfig.Threshold, ct);
            }

            if (result.Found)
            {
                return result;
            }

            if (result.Error == null && (best == null || result.Confidence > best.Confidence))
            {
                best = result;
            }
        }

        return best ?? DetectionResult.Failed("Template does not fit in the search area at any scale", imageConfig);
    }

    private static IEnumerable<double> BuildScaleLadder(MultiScaleConfig config)
    {
        var steps = Math.Clamp(config.Steps, 2, 15);
        var min = Math.Clamp(Math.Min(config.MinScale, config.MaxScale), 0.2, 1.0);
        var max = Math.Clamp(Math.Max(config.MinScale, config.MaxScale), 1.0, 4.0);

        var scales = new List<double> { 1.0 };
        for (int i = 0; i < steps; i++)
        {
            var scale = min + (max - min) * i / (steps - 1);
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                scales.Add(scale);
            }
        }

        // Native scale first, then nearest scales outward, so exact matches
        // exit early and pay nothing extra.
        return scales.OrderBy(s => Math.Abs(s - 1.0));
    }
}
