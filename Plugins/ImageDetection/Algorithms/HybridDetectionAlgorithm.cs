using Emgu.CV;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

public sealed class HybridDetectionAlgorithm(double minScale = 0.5, double maxScale = 2, int steps = 13) : IDetectionAlgorithm, IDisposable
{
    public const string AlgorithmId = "hybrid-cascade";
    private readonly PyramidMatchingAlgorithm _gray = new(minScale, maxScale, steps);
    private readonly PyramidMatchingAlgorithm _shape = new(minScale, maxScale, steps, gradients: true);
    private readonly PreparedFeatureMatchingAlgorithm _features = new("sift");
    private readonly ChromaticMatchingAlgorithm _color = new(minScale, maxScale, steps);
    private readonly object _sync = new();
    public string Id => AlgorithmId;
    public string Name => "Hybrid cascade (experimental)";
    public string Description => "Cached color silhouettes, grayscale and gradient search, followed by SIFT geometry for larger targets.";
    public bool SupportsFuzzyMatching => true;
    public bool IsAvailable() => true;
    public string? GetUnavailableReason() => null;

    public DetectionResult Detect(Mat screenshot, Mat template, double threshold, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!double.IsFinite(threshold) || threshold is < 0 or > 1)
                return DetectionResult.Failed("Threshold must be between zero and one.");
            var result = _color.Detect(screenshot, template, Math.Max(0.75, threshold), cancellationToken);
            if (result.Error == null && !result.Found)
            {
                result = _gray.Detect(screenshot, template, Math.Min(template.Width, template.Height) >= 48 ? Math.Max(0.68, threshold - 0.07) : Math.Max(0.77, threshold), cancellationToken);
                if (result.Found && result.Confidence < 0.8 && !Images.ColorAgrees(screenshot, template, result)) result.Found = false;
            }
            if (result.Error == null && !result.Found)
            {
                var shape = _shape.Detect(screenshot, template, Math.Max(0.75, threshold), cancellationToken);
                if (shape.Found || shape.Error != null || shape.Confidence > result.Confidence) result = shape;
                if (!result.Found && result.Error == null && Math.Min(template.Width, template.Height) >= 48)
                {
                    var feature = _features.Detect(screenshot, template, Math.Max(0.65, threshold), cancellationToken);
                    if (feature.Found || feature.Error != null) result = feature;
                }
            }
            result.Threshold = threshold;
            result.AlgorithmId = Id;
            return result;
        }
    }


    public void Dispose()
    {
        lock (_sync) { _gray.Dispose(); _shape.Dispose(); _features.Dispose(); _color.Dispose(); }
    }
}
