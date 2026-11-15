using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

public sealed class PyramidMatchingAlgorithm(double minScale, double maxScale, int steps, bool gradients = false, bool sparse = false) : IDetectionAlgorithm, IDisposable
{
    private readonly Dictionary<string, Target> _targets = new();
    private bool _disposed;
    public string Id => gradients ? "pyramid-gradient" : "pyramid-gray";
    public string Name => gradients ? "Pyramid gradient" : "Pyramid grayscale";
    public string Description => "Cached scale search on a smaller image, followed by full-resolution verification.";
    public bool SupportsFuzzyMatching => true;
    public bool IsAvailable() => true;
    public string? GetUnavailableReason() => null;

    public DetectionResult Detect(Mat screenshot, Mat template, double threshold, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (template.IsEmpty || screenshot.IsEmpty || template.Depth != DepthType.Cv8U || screenshot.Depth != DepthType.Cv8U ||
            template.NumberOfChannels is not (1 or 3 or 4) || screenshot.NumberOfChannels is not (1 or 3 or 4))
            return DetectionResult.Failed("Pyramid matching requires nonempty 8-bit images.");
        if (!double.IsFinite(threshold) || threshold is < 0 or > 1) return DetectionResult.Failed("Threshold must be between zero and one.");
        var key = Images.Fingerprint(template);
        if (!_targets.TryGetValue(key, out var target))
        {
            if (_targets.Count >= 16)
            {
                var oldest = _targets.First(); oldest.Value.Dispose(); _targets.Remove(oldest.Key);
            }
            target = new Target(template, minScale, maxScale, steps, gradients);
            _targets.Add(key, target);
        }
        using var gray = Images.Gray(screenshot);
        if (gradients) Gradient(gray);
        var result = target.Detect(gray, threshold, cancellationToken, sparse);
        result.AlgorithmId = Id;
        return result;
    }

    private static void Gradient(Mat image)
    {
        using var dx = new Mat(); using var dy = new Mat(); using var magnitude = new Mat(); using var angle = new Mat();
        CvInvoke.GaussianBlur(image, image, new System.Drawing.Size(3, 3), 0.6);
        CvInvoke.Sobel(image, dx, DepthType.Cv32F, 1, 0, 3);
        CvInvoke.Sobel(image, dy, DepthType.Cv32F, 0, 1, 3);
        CvInvoke.CartToPolar(dx, dy, magnitude, angle);
        magnitude.ConvertTo(image, DepthType.Cv8U, 0.25);
    }

    public void Dispose()
    {
        foreach (var target in _targets.Values) target.Dispose();
        _targets.Clear(); _disposed = true;
    }

    private sealed record Variant(Mat Image, Mat Mask, double Scale) : IDisposable
    {
        public void Dispose() { Image.Dispose(); Mask.Dispose(); }
    }

    private sealed class Target : IDisposable
    {
        private readonly List<Variant> _variants = [];
        private readonly Dictionary<double, List<Mat>> _coarse = new();

        public Target(Mat source, double minScale, double maxScale, int steps, bool gradients)
        {
            if (!double.IsFinite(minScale) || !double.IsFinite(maxScale) || minScale is < 0.2 or > 1 || maxScale is < 1 or > 4 || steps is < 2 or > 15)
                throw new ArgumentException("Pyramid matching uses scales 0.2-4 and 2-15 steps.");
            using var gray = Images.Gray(source);
            using var alpha = new Mat();
            if (source.NumberOfChannels == 4) CvInvoke.ExtractChannel(source, alpha, 3);
            try
            {
                var scales = Enumerable.Range(0, steps).Select(i => minScale + (maxScale - minScale) * i / (steps - 1)).Append(1);
                int shortSide = Math.Min(gray.Width, gray.Height);
                if (shortSide <= 48)
                {
                    int first = Math.Max(2, (int)Math.Ceiling(shortSide * minScale));
                    int last = (int)Math.Floor(shortSide * maxScale);
                    scales = scales.Concat(Enumerable.Range(first, last - first + 1).Select(size => size / (double)shortSide));
                }
                var sizes = new HashSet<System.Drawing.Size>();
                foreach (double scale in scales.Distinct())
                {
                    var image = new Mat(); var mask = new Mat();
                    try
                    {
                        var size = new System.Drawing.Size(Math.Max(2, (int)Math.Round(gray.Width * scale)), Math.Max(2, (int)Math.Round(gray.Height * scale)));
                        if (!sizes.Add(size)) { image.Dispose(); mask.Dispose(); continue; }
                        CvInvoke.Resize(gray, image, size, 0, 0, scale < 1 ? Inter.Area : Inter.Cubic);
                        if (gradients) Gradient(image);
                        if (!alpha.IsEmpty)
                        {
                            CvInvoke.Resize(alpha, mask, size, 0, 0, Inter.Nearest);
                            CvInvoke.Threshold(mask, mask, 100, 255, ThresholdType.Binary);
                            if (CvInvoke.CountNonZero(mask) == mask.Rows * mask.Cols) { mask.Dispose(); mask = new Mat(); }
                        }
                        _variants.Add(new Variant(image, mask, scale));
                    }
                    catch { image.Dispose(); mask.Dispose(); throw; }
                }
            }
            catch { Dispose(); throw; }
        }

        public DetectionResult Detect(Mat gray, double threshold, CancellationToken ct, bool sparse)
        {
            double factor = Math.Min(1, Math.Max(384.0 / Math.Max(gray.Width, gray.Height), 6.0 / _variants.Min(v => Math.Min(v.Image.Width, v.Image.Height))));
            if (!_coarse.TryGetValue(factor, out var prepared))
            {
                if (_coarse.Count >= 2)
                {
                    var oldest = _coarse.First(); foreach (var image in oldest.Value) image.Dispose(); _coarse.Remove(oldest.Key);
                }
                prepared = [];
                try
                {
                    foreach (var variant in _variants)
                    {
                        var small = new Mat();
                        CvInvoke.Resize(variant.Image, small, new System.Drawing.Size(), factor, factor, Inter.Area);
                        prepared.Add(small);
                    }
                    _coarse.Add(factor, prepared);
                }
                catch { foreach (var image in prepared) image.Dispose(); throw; }
            }
            using var scene = new Mat();
            CvInvoke.Resize(gray, scene, new System.Drawing.Size(), factor, factor, Inter.Area);
            var support = sparse ? CvInvoke.BoundingRectangle(scene) : new Rectangle(System.Drawing.Point.Empty, scene.Size);
            if (support.Width == 0 || support.Height == 0) return DetectionResult.NotFound(0, threshold);
            var candidates = new List<(int Index, System.Drawing.Point Location, double Score)>();
            var peaks = new List<(int Index, System.Drawing.Point Location, double Score)>[prepared.Count];
            Parallel.For(0, prepared.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(2, Environment.ProcessorCount), CancellationToken = ct }, i =>
            {
                ct.ThrowIfCancellationRequested();
                var matches = new List<(int Index, System.Drawing.Point Location, double Score)>(3);
                peaks[i] = matches;
                var small = prepared[i]; var full = _variants[i].Image;
                if (small.Width > scene.Width || small.Height > scene.Height || full.Width > gray.Width || full.Height > gray.Height) return;
                MCvScalar mean = default, deviation = default;
                CvInvoke.MeanStdDev(small, ref mean, ref deviation);
                if (deviation.V0 < 1) return;
                using var map = new Mat();
                // Windows entirely outside the nonzero support cannot produce a positive correlation.
                int left = Math.Max(0, support.Left - small.Width + 1);
                int top = Math.Max(0, support.Top - small.Height + 1);
                int right = Math.Min(scene.Width, support.Right + small.Width - 1);
                int bottom = Math.Min(scene.Height, support.Bottom + small.Height - 1);
                using var region = new Mat(scene, new Rectangle(left, top, right - left, bottom - top));
                CvInvoke.MatchTemplate(region, small, map, TemplateMatchingType.CcoeffNormed);
                for (int peak = 0; peak < 3; peak++)
                {
                    double min = 0, max = 0; System.Drawing.Point minPoint = default, maxPoint = default;
                    CvInvoke.MinMaxLoc(map, ref min, ref max, ref minPoint, ref maxPoint);
                    if (!double.IsFinite(max) || max < 0.35) break;
                    matches.Add((i, new System.Drawing.Point(maxPoint.X + left, maxPoint.Y + top), max));
                    var box = Rectangle.Intersect(new Rectangle(maxPoint.X - small.Width / 2, maxPoint.Y - small.Height / 2,
                        small.Width, small.Height), new Rectangle(System.Drawing.Point.Empty, map.Size));
                    using var suppressed = new Mat(map, box); suppressed.SetTo(new MCvScalar(-1));
                }
            });
            foreach (var matches in peaks) candidates.AddRange(matches);
            using var map = new Mat();
            var best = DetectionResult.NotFound(0, threshold);
            foreach (var candidate in candidates.OrderByDescending(c => c.Score).Take(24))
            {
                ct.ThrowIfCancellationRequested();
                var variant = _variants[candidate.Index];
                int padding = (int)Math.Ceiling(2 / factor);
                int x = Math.Max(0, (int)Math.Round(candidate.Location.X / factor) - padding);
                int y = Math.Max(0, (int)Math.Round(candidate.Location.Y / factor) - padding);
                int width = Math.Min(gray.Width - x, variant.Image.Width + padding * 2);
                int height = Math.Min(gray.Height - y, variant.Image.Height + padding * 2);
                if (width < variant.Image.Width || height < variant.Image.Height) continue;
                using var roi = new Mat(gray, new Rectangle(x, y, width, height));
                if (!variant.Mask.IsEmpty && CvInvoke.CountNonZero(variant.Mask) < 8) continue;
                CvInvoke.MatchTemplate(roi, variant.Image, map, TemplateMatchingType.CcoeffNormed, variant.Mask.IsEmpty ? null : variant.Mask);
                using var valid = new Mat();
                using var lower = new ScalarArray(new MCvScalar(-1.00001));
                using var upper = new ScalarArray(new MCvScalar(1.00001));
                CvInvoke.InRange(map, lower, upper, valid);
                if (CvInvoke.CountNonZero(valid) == 0) continue;
                double min = 0, max = 0; System.Drawing.Point minPoint = default, maxPoint = default;
                CvInvoke.MinMaxLoc(map, ref min, ref max, ref minPoint, ref maxPoint, valid);
                double confidence = Math.Clamp(max, 0, 1);
                if (confidence > best.Confidence)
                    best = new DetectionResult { Found = confidence >= threshold, Confidence = confidence, Threshold = threshold,
                        MatchLocation = new Models.Point(x + maxPoint.X, y + maxPoint.Y), MatchSize = new Models.Size(variant.Image.Width, variant.Image.Height) };
                if (best.Found) return best;
            }
            return best;
        }

        public void Dispose()
        {
            foreach (var variant in _variants) variant.Dispose();
            foreach (var bank in _coarse.Values) foreach (var image in bank) image.Dispose();
            _variants.Clear(); _coarse.Clear();
        }
    }
}
