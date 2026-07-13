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

    /// <summary>
    /// Decomposes the enabled sections into maximal pixel rectangles for the
    /// given capture size (greedy row-major merge). Matching inside these
    /// rectangles instead of a blacked-out full frame avoids false matches in
    /// disabled areas and skips the disabled area's matching cost entirely.
    /// Note: a match must fit within one contiguous rectangular block of
    /// enabled sections.
    /// </summary>
    public List<System.Drawing.Rectangle> GetEnabledRectangles(int width, int height)
    {
        var rects = new List<System.Drawing.Rectangle>();
        int sectionWidth = width / 3;
        int sectionHeight = height / 3;
        var used = new bool[3, 3];

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                if (!this[row, col] || used[row, col]) continue;

                int spanCols = 1;
                while (col + spanCols < 3 && this[row, col + spanCols] && !used[row, col + spanCols])
                    spanCols++;

                int spanRows = 1;
                while (row + spanRows < 3 && RowRangeAvailable(used, row + spanRows, col, spanCols))
                    spanRows++;

                for (int r = row; r < row + spanRows; r++)
                    for (int c = col; c < col + spanCols; c++)
                        used[r, c] = true;

                int x = col * sectionWidth;
                int y = row * sectionHeight;
                int right = (col + spanCols == 3) ? width : (col + spanCols) * sectionWidth;
                int bottom = (row + spanRows == 3) ? height : (row + spanRows) * sectionHeight;
                rects.Add(new System.Drawing.Rectangle(x, y, right - x, bottom - y));
            }
        }

        return rects;
    }

    private bool RowRangeAvailable(bool[,] used, int row, int colStart, int spanCols)
    {
        for (int c = colStart; c < colStart + spanCols; c++)
        {
            if (!this[row, c] || used[row, c]) return false;
        }
        return true;
    }
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

    /// <summary>
    /// Capture dimensions the custom region coordinates refer to.
    /// Null on configs saved before this field existed; those are treated as
    /// already being in the current capture's pixel space (legacy behavior)
    /// and get stamped with the actual capture size on first use.
    /// </summary>
    public Resolution? ReferenceResolution { get; set; }

    /// <summary>
    /// Returns the custom region scaled from its reference resolution to the
    /// actual capture dimensions, so regions keep pointing at the same UI
    /// element after a monitor/resolution change.
    /// </summary>
    public ScreenRegion? GetCustomRegionFor(int actualWidth, int actualHeight)
    {
        if (CustomRegion == null) return null;

        var reference = ReferenceResolution;
        if (reference == null || reference.Width <= 0 || reference.Height <= 0
            || (reference.Width == actualWidth && reference.Height == actualHeight))
        {
            return CustomRegion;
        }

        double scaleX = (double)actualWidth / reference.Width;
        double scaleY = (double)actualHeight / reference.Height;

        return new ScreenRegion(
            (int)Math.Round(CustomRegion.X * scaleX),
            (int)Math.Round(CustomRegion.Y * scaleY),
            Math.Max(1, (int)Math.Round(CustomRegion.Width * scaleX)),
            Math.Max(1, (int)Math.Round(CustomRegion.Height * scaleY)),
            CustomRegion.Name);
    }
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
