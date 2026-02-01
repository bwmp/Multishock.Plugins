using System.Text.Json;
using System.Text.Json.Serialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ImageDetection.Models;
using MultiShock.PluginSdk;

namespace ImageDetection.Services;

/// <summary>
/// Service for managing image detection configuration and persistence.
/// </summary>
public class ImageConfigService
{
    private const string PluginId = "com.multishock.imagedetection";
    private const string ConfigFileName = "detection-config.json";
    private const string ModulesFolderName = "modules";

    private readonly IPluginHost _pluginHost;
    private readonly string _dataPath;
    private readonly string _configPath;
    private readonly string _modulesPath;

    private readonly object _lock = new();
    private ConfigState _state = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Event raised when configuration changes.
    /// </summary>
    public event Action? ConfigurationChanged;

    public ImageConfigService(IPluginHost pluginHost)
    {
        _pluginHost = pluginHost;

        _dataPath = _pluginHost.GetPluginDataPath(PluginId);
        _configPath = Path.Combine(_dataPath, ConfigFileName);
        _modulesPath = Path.Combine(_dataPath, ModulesFolderName);

        Directory.CreateDirectory(_dataPath);
        Directory.CreateDirectory(_modulesPath);

        LoadConfig();
    }

    #region Global Settings

    /// <summary>
    /// Global capture configuration.
    /// </summary>
    public CaptureConfig CaptureConfig
    {
        get => _state.CaptureConfig;
        set { _state.CaptureConfig = value; SaveConfig(); }
    }

    /// <summary>
    /// Default algorithm ID for new images.
    /// </summary>
    public string DefaultAlgorithmId
    {
        get => _state.DefaultAlgorithmId;
        set { _state.DefaultAlgorithmId = value; SaveConfig(); }
    }

    /// <summary>
    /// Whether background detection is enabled.
    /// </summary>
    public bool BackgroundDetectionEnabled
    {
        get => _state.BackgroundDetectionEnabled;
        set { _state.BackgroundDetectionEnabled = value; SaveConfig(); }
    }

    /// <summary>
    /// Debug mode - saves detected images, extra logging.
    /// </summary>
    public bool DebugMode
    {
        get => _state.DebugMode;
        set { _state.DebugMode = value; SaveConfig(); }
    }

    /// <summary>
    /// Gets the path to the modules directory.
    /// </summary>
    public string ModulesDirectory => _modulesPath;

    #endregion

    #region Module Management

    /// <summary>
    /// Gets all modules.
    /// </summary>
    public IReadOnlyDictionary<string, DetectionModule> Modules => _state.Modules;

    /// <summary>
    /// Gets a module by ID.
    /// </summary>
    public DetectionModule? GetModule(string moduleId)
    {
        lock (_lock)
        {
            return _state.Modules.TryGetValue(moduleId, out var module) ? module : null;
        }
    }

    /// <summary>
    /// Creates a new module.
    /// </summary>
    public DetectionModule CreateModule(string name, string? description = null)
    {
        lock (_lock)
        {
            var id = SanitizeId(name);
            var originalId = id;
            var counter = 1;

            while (_state.Modules.ContainsKey(id))
            {
                id = $"{originalId}-{counter++}";
            }

            var module = new DetectionModule
            {
                Id = id,
                Name = name,
                Description = description,
                Enabled = true
            };

            var modulePath = Path.Combine(_modulesPath, id);
            Directory.CreateDirectory(modulePath);

            _state.Modules[id] = module;
            SaveConfig();

            return module;
        }
    }

    /// <summary>
    /// Updates a module's settings.
    /// </summary>
    public void UpdateModule(DetectionModule module)
    {
        lock (_lock)
        {
            if (!_state.Modules.ContainsKey(module.Id))
            {
                throw new InvalidOperationException($"Module not found: {module.Id}");
            }

            module.ModifiedAt = DateTime.UtcNow;
            _state.Modules[module.Id] = module;
            SaveConfig();
        }
    }

    /// <summary>
    /// Deletes a module and all its images.
    /// </summary>
    public void DeleteModule(string moduleId)
    {
        lock (_lock)
        {
            if (!_state.Modules.Remove(moduleId))
            {
                return;
            }

            var modulePath = Path.Combine(_modulesPath, moduleId);
            if (Directory.Exists(modulePath))
            {
                Directory.Delete(modulePath, true);
            }

            SaveConfig();
        }
    }

    /// <summary>
    /// Sets a module's enabled state.
    /// </summary>
    public void SetModuleEnabled(string moduleId, bool enabled)
    {
        lock (_lock)
        {
            if (_state.Modules.TryGetValue(moduleId, out var module))
            {
                module.Enabled = enabled;
                module.ModifiedAt = DateTime.UtcNow;
                SaveConfig();
            }
        }
    }

    #endregion

    #region Image Management

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

    #endregion

    #region Image Loading

    /// <summary>
    /// Loads an image from disk and scales it to the target resolution.
    /// </summary>
    public Mat? LoadImage(DetectionImage imageConfig, Resolution targetResolution)
    {
        try
        {
            if (!File.Exists(imageConfig.FilePath))
            {
                return null;
            }

            var image = CvInvoke.Imread(imageConfig.FilePath, ImreadModes.Unchanged);

            if (image.IsEmpty)
            {
                return null;
            }

            if (imageConfig.AutoResize)
            {
                var (scaleX, scaleY) = imageConfig.CaptureResolution.GetScaleRatios(targetResolution);

                if (Math.Abs(scaleX - 1.0) > 0.001 || Math.Abs(scaleY - 1.0) > 0.001)
                {
                    var resized = new Mat();
                    CvInvoke.Resize(image, resized, new System.Drawing.Size(0, 0), scaleX, scaleY, Inter.Area);
                    image.Dispose();
                    image = resized;
                }
            }

            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the thumbnail path for an image (creates if needed).
    /// </summary>
    public string? GetThumbnailPath(DetectionImage image, int maxSize = 100)
    {
        try
        {
            var thumbDir = Path.Combine(_dataPath, "thumbnails");
            Directory.CreateDirectory(thumbDir);

            var thumbPath = Path.Combine(thumbDir, $"{Path.GetFileNameWithoutExtension(image.FilePath)}_thumb.png");

            if (File.Exists(thumbPath))
            {
                var originalTime = File.GetLastWriteTime(image.FilePath);
                var thumbTime = File.GetLastWriteTime(thumbPath);

                if (thumbTime >= originalTime)
                {
                    return thumbPath;
                }
            }

            using var original = CvInvoke.Imread(image.FilePath, ImreadModes.Unchanged);
            if (original.IsEmpty) return null;

            double scale = Math.Min((double)maxSize / original.Width, (double)maxSize / original.Height);
            scale = Math.Min(scale, 1.0);

            using var thumb = new Mat();
            CvInvoke.Resize(original, thumb, new System.Drawing.Size(0, 0), scale, scale, Inter.Area);
            CvInvoke.Imwrite(thumbPath, thumb);

            return thumbPath;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Persistence

    private void LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _state = JsonSerializer.Deserialize<ConfigState>(json, JsonOptions) ?? new ConfigState();
                }
                else
                {
                    _state = new ConfigState();
                    SaveConfig();
                }

                ScanModulesFolder();
            }
            catch
            {
                _state = new ConfigState();
            }
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(_configPath, json);
            ConfigurationChanged?.Invoke();
        }
        catch
        {
            // Failed to save configuration
        }
    }

    private void ScanModulesFolder()
    {
        if (!Directory.Exists(_modulesPath)) return;

        foreach (var moduleDir in Directory.GetDirectories(_modulesPath))
        {
            var moduleId = Path.GetFileName(moduleDir);

            if (!_state.Modules.ContainsKey(moduleId))
            {
                var module = new DetectionModule
                {
                    Id = moduleId,
                    Name = moduleId,
                    Enabled = true
                };
                _state.Modules[moduleId] = module;
            }

            var module2 = _state.Modules[moduleId];
            foreach (var imagePath in Directory.GetFiles(moduleDir, "*.png"))
            {
                var imageId = Path.GetFileName(imagePath);

                if (!module2.Images.ContainsKey(imageId))
                {
                    var image = new DetectionImage
                    {
                        Id = imageId,
                        Name = Path.GetFileNameWithoutExtension(imagePath),
                        FilePath = imagePath,
                        Enabled = true,
                        AlgorithmId = DefaultAlgorithmId
                    };
                    module2.Images[imageId] = image;
                }
                else
                {
                    module2.Images[imageId].FilePath = imagePath;
                }
            }
        }
    }

    private static string SanitizeId(string name)
    {
        var sanitized = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(c.ToString(), "");
        }

        return sanitized;
    }

    #endregion

    #region Config State

    private class ConfigState
    {
        public CaptureConfig CaptureConfig { get; set; } = new();
        public string DefaultAlgorithmId { get; set; } = "template-matching";
        public bool BackgroundDetectionEnabled { get; set; } = false;
        public bool DebugMode { get; set; } = false;
        public Dictionary<string, DetectionModule> Modules { get; set; } = [];
    }

    #endregion
}
