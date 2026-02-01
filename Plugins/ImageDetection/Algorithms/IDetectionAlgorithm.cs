using Emgu.CV;
using ImageDetection.Models;

namespace ImageDetection.Algorithms;

/// <summary>
/// Interface for pluggable image detection algorithms.
/// Implement this interface to add new detection methods.
/// </summary>
public interface IDetectionAlgorithm
{
    /// <summary>
    /// Unique identifier for this algorithm.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name for the algorithm.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of how the algorithm works and when to use it.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this algorithm supports partial/fuzzy matching.
    /// </summary>
    bool SupportsFuzzyMatching { get; }

    /// <summary>
    /// Performs image detection.
    /// </summary>
    /// <param name="screenshot">The screenshot to search in.</param>
    /// <param name="template">The template image to find.</param>
    /// <param name="threshold">Match threshold (0.0 - 1.0). Higher = stricter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detection result with match information.</returns>
    DetectionResult Detect(
        Mat screenshot,
        Mat template,
        double threshold,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the algorithm can be used (e.g., required dependencies are available).
    /// </summary>
    /// <returns>True if the algorithm is available, false otherwise.</returns>
    bool IsAvailable();

    /// <summary>
    /// Gets the reason why the algorithm is not available (if IsAvailable returns false).
    /// </summary>
    string? GetUnavailableReason();
}

/// <summary>
/// Registry of available detection algorithms.
/// </summary>
public class AlgorithmRegistry
{
    private readonly Dictionary<string, IDetectionAlgorithm> _algorithms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an algorithm.
    /// </summary>
    public void Register(IDetectionAlgorithm algorithm)
    {
        _algorithms[algorithm.Id] = algorithm;
    }

    /// <summary>
    /// Gets an algorithm by ID.
    /// </summary>
    public IDetectionAlgorithm? Get(string id)
    {
        return _algorithms.TryGetValue(id, out var algorithm) ? algorithm : null;
    }

    /// <summary>
    /// Gets all registered algorithms.
    /// </summary>
    public IEnumerable<IDetectionAlgorithm> GetAll() => _algorithms.Values;

    /// <summary>
    /// Gets all available (usable) algorithms.
    /// </summary>
    public IEnumerable<IDetectionAlgorithm> GetAvailable() => _algorithms.Values.Where(a => a.IsAvailable());

    /// <summary>
    /// Gets the default algorithm.
    /// </summary>
    public IDetectionAlgorithm? GetDefault() => Get("template-matching");
}
