using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Util;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

internal sealed class PreparedFeatureTarget : IDisposable
{
    private readonly string method;
    private readonly int _width;
    private readonly int _height;
    private readonly Feature2D detector;
    private readonly VectorOfKeyPoint targetKeys = new();
    private readonly Mat targetDescriptors = new();
    private string Id => method;
    public int FeatureCount => targetKeys.Size;
    public double Coverage { get; }

    public PreparedFeatureTarget(Mat template, string method)
    {
        this.method = method;
        _width = template.Width;
        _height = template.Height;
        detector = method switch
        {
            "orb" => new ORB(2000, edgeThreshold: 10, patchSize: 21),
            "sift" => new SIFT(2000),
            "akaze" => new AKAZE(),
            "brisk" => new Brisk(),
            _ => throw new ArgumentException("Unsupported feature method.", nameof(method))
        };
        try
        {
            using var target = Images.Gray(template);
            using var mask = new Mat();
            if (template.NumberOfChannels == 4)
            {
                CvInvoke.ExtractChannel(template, mask, 3);
                CvInvoke.Threshold(mask, mask, 100, 255, ThresholdType.Binary);
            }
            detector.DetectAndCompute(target, mask.IsEmpty ? null : mask, targetKeys, targetDescriptors, false);
            if (targetKeys.Size >= 3)
            {
                using var points = new VectorOfPointF(targetKeys.ToArray().Select(k => k.Point).ToArray());
                using var hull = new VectorOfPointF();
                CvInvoke.ConvexHull(points, hull);
                Coverage = CvInvoke.ContourArea(hull) / (_width * (double)_height);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        detector.Dispose();
        targetKeys.Dispose();
        targetDescriptors.Dispose();
    }

    public DetectionResult Detect(Mat screenshot, double threshold, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var miss = DetectionResult.NotFound(0, threshold, algorithmId: Id);
        if (targetDescriptors.Rows < 8) return miss;
        using var scene = Images.Gray(screenshot);
        using var sceneKeys = new VectorOfKeyPoint();
        using var sceneDescriptors = new Mat();
        detector.DetectAndCompute(scene, null, sceneKeys, sceneDescriptors, false);
        if (sceneDescriptors.Rows < 8)
            return miss;

        cancellationToken.ThrowIfCancellationRequested();
        using var matcher = new BFMatcher(method == "sift" ? DistanceType.L2 : DistanceType.Hamming);
        using var matches = new VectorOfVectorOfDMatch();
        matcher.KnnMatch(targetDescriptors, sceneDescriptors, matches, 2);
        var candidates = matches.ToArrayOfArray()
            .Where(pair => pair.Length == 2 && pair[0].Distance < 0.75 * pair[1].Distance)
            .Select(pair => pair[0]).OrderBy(m => m.Distance).DistinctBy(m => m.TrainIdx).ToArray();
        if (candidates.Length < 8)
            return miss;

        var sourceKeys = targetKeys.ToArray();
        var destKeys = sceneKeys.ToArray();
        var source = candidates.Select(m => sourceKeys[m.QueryIdx].Point).ToArray();
        var destination = candidates.Select(m => destKeys[m.TrainIdx].Point).ToArray();
        using var inliers = new Mat();
        using var homography = CvInvoke.FindHomography(source, destination, RobustEstimationAlgorithm.Ransac, 3, inliers);
        if (homography.IsEmpty || CvInvoke.CountNonZero(inliers) < 8)
            return miss;

        var inlierSource = source.Where((_, i) => System.Runtime.InteropServices.Marshal.ReadByte(inliers.DataPointer, i) != 0).ToArray();
        using var hull = new VectorOfPointF();
        using var sourcePoints = new VectorOfPointF(inlierSource);
        CvInvoke.ConvexHull(sourcePoints, hull);
        // A cluster of matching letters or repeated texture must not validate the entire target.
        if (CvInvoke.ContourArea(hull) < _width * _height * 0.1)
            return miss;

        PointF[] corners = [new(0, 0), new(_width, 0), new(_width, _height), new(0, _height)];
        var projected = CvInvoke.PerspectiveTransform(corners, homography);
        if (projected.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X > scene.Width || p.Y > scene.Height))
            return miss;
        using var polygon = new VectorOfPointF(projected);
        if (!CvInvoke.IsContourConvex(polygon) || CvInvoke.ContourArea(polygon, true) < 16)
            return miss;

        var x = (int)Math.Floor(projected.Min(p => p.X));
        var y = (int)Math.Floor(projected.Min(p => p.Y));
        var width = (int)Math.Ceiling(projected.Max(p => p.X)) - x;
        var height = (int)Math.Ceiling(projected.Max(p => p.Y)) - y;
        double confidence = (double)inlierSource.Length / candidates.Length * Math.Min(1, inlierSource.Length / 15.0);
        cancellationToken.ThrowIfCancellationRequested();
        return new DetectionResult
        {
            Found = confidence >= threshold, Confidence = confidence, Threshold = threshold, AlgorithmId = Id,
            MatchLocation = new Models.Point(x, y), MatchSize = new Models.Size(width, height)
        };
    }
}

