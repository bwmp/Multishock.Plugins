using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

public sealed class ChromaticMatchingAlgorithm(double minScale, double maxScale, int steps) : IDetectionAlgorithm, IDisposable
{
    private readonly PyramidMatchingAlgorithm _matcher = new(minScale, maxScale, steps, sparse: true);
    private readonly Dictionary<string, Target> _targets = new();
    private bool _disposed;
    public string Id => "chromatic-shape";
    public string Name => "Chromatic shape (experimental)";
    public string Description => "Matches the silhouette of a target's dominant saturated color across scales.";
    public bool SupportsFuzzyMatching => true;
    public bool IsAvailable() => true;
    public string? GetUnavailableReason() => null;

    public DetectionResult Detect(Mat screenshot, Mat template, double threshold, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (template.IsEmpty || screenshot.IsEmpty || template.Depth != DepthType.Cv8U || screenshot.Depth != DepthType.Cv8U ||
            template.NumberOfChannels is not (1 or 3 or 4) || screenshot.NumberOfChannels is not (1 or 3 or 4))
            return DetectionResult.Failed("Chromatic matching requires nonempty 8-bit images.");
        if (!double.IsFinite(threshold) || threshold is < 0 or > 1) return DetectionResult.Failed("Threshold must be between zero and one.");
        var key = Images.Fingerprint(template);
        if (!_targets.TryGetValue(key, out var target))
        {
            if (_targets.Count >= 16)
            {
                var oldest = _targets.First(); oldest.Value.Dispose(); _targets.Remove(oldest.Key);
            }
            target = Prepare(template);
            _targets.Add(key, target);
        }
        if (target.Hue < 0) return DetectionResult.NotFound(0, threshold, algorithmId: Id);
        using var hsv = Hsv(screenshot);
        using var foreground = Mask(hsv, target.Hue);
        var result = _matcher.Detect(foreground, target.Mask, threshold, cancellationToken);
        if (result.Found && result.Confidence < 0.8 && !Images.ColorAgrees(screenshot, template, result, 0.5)) result.Found = false;
        if (result.Found && result.MatchLocation is { } location && result.MatchSize is { } size)
        {
            var bounds = target.Bounds;
            int x = (int)Math.Floor(bounds.X * size.Width / (double)target.Mask.Width);
            int y = (int)Math.Floor(bounds.Y * size.Height / (double)target.Mask.Height);
            int right = (int)Math.Ceiling(bounds.Right * size.Width / (double)target.Mask.Width);
            int bottom = (int)Math.Ceiling(bounds.Bottom * size.Height / (double)target.Mask.Height);
            result.MatchLocation = new Models.Point(location.X + x, location.Y + y);
            result.MatchSize = new Models.Size(right - x, bottom - y);
        }
        result.AlgorithmId = Id;
        return result;
    }

    private static Target Prepare(Mat source)
    {
        using var hsv = Hsv(source);
        using var pixels = hsv.ToImage<Hsv, byte>();
        using var alpha = new Mat();
        if (source.NumberOfChannels == 4) CvInvoke.ExtractChannel(source, alpha, 3);
        var histogram = new int[180];
        var data = pixels.Data;
        for (int y = source.Height / 5; y < source.Height * 4 / 5; y++)
        for (int x = source.Width / 5; x < source.Width * 4 / 5; x++)
            if (data[y, x, 1] >= 80 && data[y, x, 2] >= 50)
                histogram[data[y, x, 0]]++;
        int hue = Enumerable.Range(0, 180).MaxBy(h => Enumerable.Range(-8, 17).Sum(d => histogram[(h + d + 180) % 180]));
        var mask = Mask(hsv, hue);
        if (!alpha.IsEmpty)
        {
            CvInvoke.Threshold(alpha, alpha, 100, 255, ThresholdType.Binary);
            CvInvoke.BitwiseAnd(mask, alpha, mask);
        }
        double coverage = CvInvoke.CountNonZero(mask) / (double)(source.Width * source.Height);
        using var points = new VectorOfPoint();
        CvInvoke.FindNonZero(mask, points);
        var bounds = new System.Drawing.Rectangle(0, 0, source.Width, source.Height);
        if (points.Size > 0)
        {
            var foreground = CvInvoke.BoundingRectangle(points);
            foreground.Inflate(1, 1);
            bounds = System.Drawing.Rectangle.Intersect(bounds, foreground);
        }
        return new Target(coverage is >= 0.05 and <= 0.8 ? hue : -1, mask, bounds);
    }

    private static Mat Hsv(Mat source)
    {
        using var bgr = new Mat();
        var hsv = new Mat();
        try
        {
            if (source.NumberOfChannels == 3) CvInvoke.CvtColor(source, hsv, ColorConversion.Bgr2Hsv);
            else
            {
                CvInvoke.CvtColor(source, bgr, source.NumberOfChannels == 4 ? ColorConversion.Bgra2Bgr : ColorConversion.Gray2Bgr);
                CvInvoke.CvtColor(bgr, hsv, ColorConversion.Bgr2Hsv);
            }
            return hsv;
        }
        catch { hsv.Dispose(); throw; }
    }

    private static Mat Mask(Mat hsv, int hue)
    {
        var mask = new Mat();
        using var lower = new ScalarArray(new MCvScalar(Math.Max(0, hue - 12), 80, 50));
        using var upper = new ScalarArray(new MCvScalar(Math.Min(179, hue + 12), 255, 255));
        try
        {
            CvInvoke.InRange(hsv, lower, upper, mask);
            if (hue < 12 || hue > 167)
            {
                using var wrapped = new Mat();
                using var wrapLower = new ScalarArray(new MCvScalar(hue < 12 ? hue + 168 : 0, 80, 50));
                using var wrapUpper = new ScalarArray(new MCvScalar(hue < 12 ? 179 : hue - 168, 255, 255));
                CvInvoke.InRange(hsv, wrapLower, wrapUpper, wrapped);
                CvInvoke.BitwiseOr(mask, wrapped, mask);
            }
            return mask;
        }
        catch { mask.Dispose(); throw; }
    }

    private sealed record Target(int Hue, Mat Mask, System.Drawing.Rectangle Bounds) : IDisposable
    {
        public void Dispose() => Mask.Dispose();
    }

    public void Dispose()
    {
        foreach (var target in _targets.Values) target.Dispose();
        _targets.Clear(); _matcher.Dispose(); _disposed = true;
    }
}
