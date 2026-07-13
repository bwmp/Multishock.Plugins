using ImageDetection.Models;

namespace ImageDetection.Services;

public partial class ImageConfigService
{
    /// <summary>
    /// Gets an image by module and image ID.
    /// </summary>
    public DetectionImage? GetImage(string moduleId, string imageId)
    {
        lock (_lock)
        {
            if (_state.Modules.TryGetValue(moduleId, out var module))
            {
                return module.GetImage(imageId);
            }

            return null;
        }
    }

    /// <summary>
    /// Gets all enabled images across all enabled modules.
    /// </summary>
    public IEnumerable<(string ModuleId, DetectionImage Image)> GetAllEnabledImages()
    {
        lock (_lock)
        {
            foreach (var module in _state.Modules.Values)
            {
                foreach (var image in module.GetEnabledImages())
                {
                    yield return (module.Id, image);
                }
            }
        }
    }

    /// <summary>
    /// Adds a new image to a module.
    /// </summary>
    public DetectionImage AddImage(
        string moduleId,
        string fileName,
        byte[] imageData,
        Resolution captureResolution)
    {
        lock (_lock)
        {
            if (!_state.Modules.TryGetValue(moduleId, out var module))
            {
                throw new InvalidOperationException($"Module not found: {moduleId}");
            }

            var imageId = SanitizeId(Path.GetFileNameWithoutExtension(fileName));
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension)) extension = ".png";

            var originalId = imageId;
            var counter = 1;
            while (module.Images.ContainsKey(imageId + extension))
            {
                imageId = $"{originalId}-{counter++}";
            }

            var fullImageId = imageId + extension;
            var imagePath = Path.Combine(_modulesPath, moduleId, fullImageId);

            File.WriteAllBytes(imagePath, imageData);

            var image = new DetectionImage
            {
                Id = fullImageId,
                Name = imageId,
                FilePath = imagePath,
                Enabled = true,
                CaptureResolution = captureResolution,
                AlgorithmId = DefaultAlgorithmId
            };

            module.SetImage(image);
            SaveConfig();

            return image;
        }
    }

    /// <summary>
    /// Adds an existing DetectionImage to a module.
    /// </summary>
    public void AddImage(string moduleId, DetectionImage image)
    {
        lock (_lock)
        {
            if (!_state.Modules.TryGetValue(moduleId, out var module))
            {
                throw new InvalidOperationException($"Module not found: {moduleId}");
            }

            module.SetImage(image);
            SaveConfig();
        }
    }

    /// <summary>
    /// Updates an image's configuration.
    /// </summary>
    public void UpdateImage(string moduleId, DetectionImage image)
    {
        lock (_lock)
        {
            if (!_state.Modules.TryGetValue(moduleId, out var module))
            {
                throw new InvalidOperationException($"Module not found: {moduleId}");
            }

            image.ModifiedAt = DateTime.UtcNow;
            module.SetImage(image);
            SaveConfig();
        }
    }

    /// <summary>
    /// Deletes an image from a module.
    /// </summary>
    public void DeleteImage(string moduleId, string imageId)
    {
        lock (_lock)
        {
            if (!_state.Modules.TryGetValue(moduleId, out var module))
            {
                return;
            }

            var image = module.GetImage(imageId);
            if (image == null) return;

            if (File.Exists(image.FilePath))
            {
                File.Delete(image.FilePath);
            }

            module.RemoveImage(imageId);
            SaveConfig();
        }
    }

    /// <summary>
    /// Stamps a reference resolution onto custom regions saved before regions
    /// became resolution-aware. Legacy regions are defined in the pixel space
    /// of whatever the user is currently capturing, so stamping the current
    /// capture size preserves their behavior exactly while making future
    /// resolution changes scale correctly. Persists once if anything changed.
    /// </summary>
    public void StampLegacyRegionReferences(Resolution reference)
    {
        if (reference.Width <= 0 || reference.Height <= 0) return;

        lock (_lock)
        {
            var changed = false;

            foreach (var module in _state.Modules.Values)
            {
                foreach (var image in module.Images.Values)
                {
                    if (image.Region.CustomRegion != null && image.Region.ReferenceResolution == null)
                    {
                        image.Region.ReferenceResolution = new Resolution(reference.Width, reference.Height);
                        changed = true;
                    }
                }
            }

            if (changed) SaveConfig();
        }
    }

    /// <summary>
    /// Sets an image's enabled state.
    /// </summary>
    public void SetImageEnabled(string moduleId, string imageId, bool enabled)
    {
        lock (_lock)
        {
            if (_state.Modules.TryGetValue(moduleId, out var module))
            {
                var image = module.GetImage(imageId);
                if (image != null)
                {
                    image.Enabled = enabled;
                    image.ModifiedAt = DateTime.UtcNow;
                    SaveConfig();
                }
            }
        }
    }
}
