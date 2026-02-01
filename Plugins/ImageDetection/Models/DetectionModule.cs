namespace ImageDetection.Models;

/// <summary>
/// A module is a collection of related detection images (like a folder/category).
/// </summary>
public class DetectionModule
{
    /// <summary>
    /// Unique identifier for this module (typically the folder name).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the module.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this entire module is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional description for the module.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Images in this module.
    /// </summary>
    public Dictionary<string, DetectionImage> Images { get; set; } = [];

    /// <summary>
    /// When the module was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the module was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets all enabled images in this module.
    /// </summary>
    public IEnumerable<DetectionImage> GetEnabledImages()
    {
        if (!Enabled) yield break;

        foreach (var image in Images.Values)
        {
            if (image.Enabled)
                yield return image;
        }
    }

    /// <summary>
    /// Gets an image by ID.
    /// </summary>
    public DetectionImage? GetImage(string imageId)
    {
        return Images.TryGetValue(imageId, out var image) ? image : null;
    }

    /// <summary>
    /// Adds or updates an image in this module.
    /// </summary>
    public void SetImage(DetectionImage image)
    {
        Images[image.Id] = image;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes an image from this module.
    /// </summary>
    public bool RemoveImage(string imageId)
    {
        var removed = Images.Remove(imageId);
        if (removed) ModifiedAt = DateTime.UtcNow;
        return removed;
    }
}
