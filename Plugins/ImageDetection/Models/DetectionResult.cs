namespace ImageDetection.Models;

/// <summary>
/// Result of an image detection attempt.
/// </summary>
public class DetectionResult
{
    /// <summary>
    /// Whether the target image was found.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// The confidence/match value (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// The threshold that was used for detection.
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    /// Location of the best match (top-left corner).
    /// </summary>
    public Point? MatchLocation { get; set; }

    /// <summary>
    /// Size of the matched region.
    /// </summary>
    public Size? MatchSize { get; set; }

    /// <summary>
    /// The image that was being searched for.
    /// </summary>
    public DetectionImage? Image { get; set; }

    /// <summary>
    /// The algorithm that was used.
    /// </summary>
    public string? AlgorithmId { get; set; }

    /// <summary>
    /// Time taken to perform the detection.
    /// </summary>
    public TimeSpan DetectionTime { get; set; }

    /// <summary>
    /// When this detection occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional error message if detection failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Creates a successful detection result.
    /// </summary>
    public static DetectionResult Success(
        double confidence,
        double threshold,
        Point matchLocation,
        Size matchSize,
        DetectionImage? image = null,
        string? algorithmId = null)
    {
        return new DetectionResult
        {
            Found = true,
            Confidence = confidence,
            Threshold = threshold,
            MatchLocation = matchLocation,
            MatchSize = matchSize,
            Image = image,
            AlgorithmId = algorithmId
        };
    }

    /// <summary>
    /// Creates a not-found result (below threshold).
    /// </summary>
    public static DetectionResult NotFound(
        double confidence,
        double threshold,
        DetectionImage? image = null,
        string? algorithmId = null)
    {
        return new DetectionResult
        {
            Found = false,
            Confidence = confidence,
            Threshold = threshold,
            Image = image,
            AlgorithmId = algorithmId
        };
    }

    /// <summary>
    /// Creates an error result.
    /// </summary>
    public static DetectionResult Failed(string error, DetectionImage? image = null)
    {
        return new DetectionResult
        {
            Found = false,
            Error = error,
            Image = image
        };
    }
}

/// <summary>
/// Simple point structure for match locations.
/// </summary>
public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Simple size structure for match dimensions.
/// </summary>
public struct Size
{
    public int Width { get; set; }
    public int Height { get; set; }

    public Size(int width, int height)
    {
        Width = width;
        Height = height;
    }
}
