namespace ImageDetection.Models;

/// <summary>
/// Represents a region of the screen to capture/analyze.
/// </summary>
public class ScreenRegion
{
    /// <summary>
    /// X coordinate of the top-left corner (pixels).
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Y coordinate of the top-left corner (pixels).
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// Width of the region (pixels).
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height of the region (pixels).
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Optional name for this region.
    /// </summary>
    public string? Name { get; set; }

    public ScreenRegion() { }

    public ScreenRegion(int x, int y, int width, int height, string? name = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Name = name;
    }

    /// <summary>
    /// Creates a region from percentage-based coordinates (0-100).
    /// </summary>
    public static ScreenRegion FromPercentage(
        double xPercent, double yPercent,
        double widthPercent, double heightPercent,
        int screenWidth, int screenHeight,
        string? name = null)
    {
        return new ScreenRegion
        {
            X = (int)(screenWidth * xPercent / 100),
            Y = (int)(screenHeight * yPercent / 100),
            Width = (int)(screenWidth * widthPercent / 100),
            Height = (int)(screenHeight * heightPercent / 100),
            Name = name
        };
    }
}

/// <summary>
/// Configuration for 3x3 grid-based screen sections.
/// Each boolean represents whether that section is enabled for detection.
/// Grid layout:
/// [0][1][2]
/// [3][4][5]
/// [6][7][8]
/// </summary>
public class GridSections
{
    /// <summary>
    /// 9 booleans representing the 3x3 grid sections (left-to-right, top-to-bottom).
    /// </summary>
    public bool[] Sections { get; set; } = [true, true, true, true, true, true, true, true, true];

    /// <summary>
    /// Creates with all sections enabled.
    /// </summary>
    public static GridSections All() => new() { Sections = [true, true, true, true, true, true, true, true, true] };

    /// <summary>
    /// Creates with all sections disabled.
    /// </summary>
    public static GridSections None() => new() { Sections = [false, false, false, false, false, false, false, false, false] };

    /// <summary>
    /// Gets the section index for a given row and column (0-2).
    /// </summary>
    public bool this[int row, int col]
    {
        get => Sections[row * 3 + col];
        set => Sections[row * 3 + col] = value;
    }

    /// <summary>
    /// Checks if any section is enabled.
    /// </summary>
    public bool HasAnySectionEnabled() => Sections.Any(s => s);

    /// <summary>
    /// Checks if all sections are enabled.
    /// </summary>
    public bool AllSectionsEnabled() => Sections.All(s => s);
}

/// <summary>
/// Combined region configuration supporting both grid and custom regions.
/// </summary>
public class RegionConfig
{
    /// <summary>
    /// Type of region filtering to use.
    /// </summary>
    public RegionType Type { get; set; } = RegionType.FullScreen;

    /// <summary>
    /// Grid sections configuration (used when Type is Grid).
    /// </summary>
    public GridSections GridSections { get; set; } = GridSections.All();

    /// <summary>
    /// Custom region configuration (used when Type is Custom).
    /// </summary>
    public ScreenRegion? CustomRegion { get; set; }
}

/// <summary>
/// Type of screen region filtering.
/// </summary>
public enum RegionType
{
    /// <summary>
    /// Use the entire screen/capture area.
    /// </summary>
    FullScreen,

    /// <summary>
    /// Use 3x3 grid-based sections.
    /// </summary>
    Grid,

    /// <summary>
    /// Use a custom rectangular region.
    /// </summary>
    Custom
}
