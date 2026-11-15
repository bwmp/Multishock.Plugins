using Emgu.CV;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

public sealed class PreparedFeatureMatchingAlgorithm(string method) : IDetectionAlgorithm, IDisposable
{
    private readonly Dictionary<string, PreparedFeatureTarget> _targets = new();
    private bool _disposed;
    public string Id => "prepared-" + method;
    public string Name => "Prepared " + method.ToUpperInvariant();
    public string Description => "Feature matching with cached target descriptors and geometric verification.";
    public bool SupportsFuzzyMatching => true;
    public bool IsAvailable() => true;
    public string? GetUnavailableReason() => null;

    public DetectionResult Detect(Mat screenshot, Mat template, double threshold, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var key = Images.Fingerprint(template);
        if (!_targets.TryGetValue(key, out var prepared))
        {
            if (_targets.Count >= 16)
            {
                var oldest = _targets.First(); oldest.Value.Dispose(); _targets.Remove(oldest.Key);
            }
            prepared = new PreparedFeatureTarget(template, method);
            _targets.Add(key, prepared);
        }
        var result = prepared.Detect(screenshot, threshold, cancellationToken);
        result.AlgorithmId = Id;
        return result;
    }

    public void Dispose()
    {
        foreach (var target in _targets.Values) target.Dispose();
        _targets.Clear(); _disposed = true;
    }
}
