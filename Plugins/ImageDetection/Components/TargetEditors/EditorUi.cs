using ImageDetection.Models;

namespace ImageDetection.Components.TargetEditors;

/// <summary>
/// Small display/mutation helpers shared by the target editor components.
/// </summary>
internal static class EditorUi
{
    public static string GetImageDataUri(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var mime = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "image/png"
            };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Applies manual coordinate edits to a custom region. Manually typed
    /// coordinates are in the given reference resolution's pixel space, which
    /// is stamped when the region does not have one yet.
    /// </summary>
    public static void UpdateCustomRegion(
        DetectionImage image, Resolution? referenceResolution,
        int? x = null, int? y = null, int? width = null, int? height = null)
    {
        if (image.Region.CustomRegion == null) return;

        if (x.HasValue) image.Region.CustomRegion.X = x.Value;
        if (y.HasValue) image.Region.CustomRegion.Y = y.Value;
        if (width.HasValue) image.Region.CustomRegion.Width = width.Value;
        if (height.HasValue) image.Region.CustomRegion.Height = height.Value;

        image.Region.ReferenceResolution ??= referenceResolution;
    }

    public static string TruncateText(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    public static string FormatTimeAgo(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        return elapsed.TotalSeconds < 2 ? "just now"
            : elapsed.TotalSeconds < 60 ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalMinutes < 60 ? $"{(int)elapsed.TotalMinutes}m ago"
            : $"{(int)elapsed.TotalHours}h ago";
    }
}
