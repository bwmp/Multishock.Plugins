using System.IO.Compression;
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
    /// Region picker hotkey configuration.
    /// </summary>
    public HotkeyBinding PickerHotkey
    {
        get => _state.PickerHotkey;
        set { _state.PickerHotkey = value; SaveConfig(); }
    }

    /// <summary>
    /// Gets the path to the modules directory.
    /// </summary>
    public string ModulesDirectory => _modulesPath;

    /// <summary>
    /// Gets the absolute path to a module's directory.
    /// </summary>
    public string GetModulePath(string moduleId) => Path.Combine(_modulesPath, moduleId);

    /// <summary>
    /// Resolves a potentially relative file path to an absolute path.
    /// If the path is already absolute, returns it as-is.
    /// If relative, resolves it against the module's directory.
    /// </summary>
    public string ResolveFilePath(string moduleId, string filePath)
    {
        if (Path.IsPathRooted(filePath))
            return filePath;
        return Path.Combine(GetModulePath(moduleId), filePath);
    }

    /// <summary>
    /// Converts an absolute file path to a path relative to the module directory.
    /// If already relative or outside the module dir, returns as-is.
    /// </summary>
    public string MakeRelativePath(string moduleId, string absolutePath)
    {
        if (!Path.IsPathRooted(absolutePath))
            return absolutePath;

        var moduleDir = GetModulePath(moduleId) + Path.DirectorySeparatorChar;
        if (absolutePath.StartsWith(moduleDir, StringComparison.OrdinalIgnoreCase))
            return absolutePath[moduleDir.Length..];

        return absolutePath;
    }

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

    /// <summary>
    /// Saves a cropped region screenshot as a preview image for a meter target.
    /// Returns the file path of the saved preview, or null on failure.
    /// </summary>
    public string? SaveRegionPreview(string moduleId, string targetId, Mat screenshot, ScreenRegion region)
    {
        try
        {
            var previewDir = Path.Combine(GetModulePath(moduleId), "previews");
            Directory.CreateDirectory(previewDir);

            var previewPath = Path.Combine(previewDir, $"{targetId}_region.png");

            // Clamp region to screenshot bounds
            int x = Math.Max(0, Math.Min(region.X, screenshot.Width - 1));
            int y = Math.Max(0, Math.Min(region.Y, screenshot.Height - 1));
            int width = Math.Min(region.Width, screenshot.Width - x);
            int height = Math.Min(region.Height, screenshot.Height - y);

            if (width <= 0 || height <= 0) return null;

            using var subMat = new Mat(screenshot, new System.Drawing.Rectangle(x, y, width, height));
            using var cropped = subMat.Clone();
            CvInvoke.Imwrite(previewPath, cropped);

            return previewPath;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Module Export / Import

    /// <summary>
    /// Exports a module as a self-contained .zip file.
    /// The zip contains module.json (the module config) and all referenced assets
    /// (template images, meter previews) with relative paths.
    /// </summary>
    public string? ExportModule(string moduleId, string outputPath)
    {
        lock (_lock)
        {
            if (!_state.Modules.TryGetValue(moduleId, out var module))
                return null;

            try
            {
                // Create a portable copy of the module with relative paths
                var exportModule = JsonSerializer.Deserialize<DetectionModule>(
                    JsonSerializer.Serialize(module, JsonOptions), JsonOptions)!;

                foreach (var image in exportModule.Images.Values)
                {
                    if (!string.IsNullOrEmpty(image.FilePath))
                        image.FilePath = MakeRelativePath(moduleId, image.FilePath);

                    if (!string.IsNullOrEmpty(image.Meter.RegionPreviewPath))
                        image.Meter.RegionPreviewPath = MakeRelativePath(moduleId, image.Meter.RegionPreviewPath);
                }

                var zipPath = Path.Combine(outputPath, $"{moduleId}.msmodule");

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                var moduleJson = JsonSerializer.Serialize(exportModule, JsonOptions);
                var entry = zip.CreateEntry("module.json");
                using (var stream = entry.Open())
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(moduleJson);
                }

                // Bundle all referenced files from the module directory
                var moduleDir = GetModulePath(moduleId);
                if (Directory.Exists(moduleDir))
                {
                    foreach (var file in Directory.GetFiles(moduleDir, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(moduleDir, file);
                        // Skip module.json itself (we wrote our own)
                        if (relativePath.Equals("module.json", StringComparison.OrdinalIgnoreCase))
                            continue;
                        zip.CreateEntryFromFile(file, relativePath);
                    }
                }

                return zipPath;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Imports a module from a .msmodule (zip) file.
    /// Extracts assets into the modules directory and registers the module.
    /// Returns the imported module or null on failure.
    /// </summary>
    public DetectionModule? ImportModule(string zipPath)
    {
        lock (_lock)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var moduleEntry = zip.GetEntry("module.json");
                if (moduleEntry == null) return null;

                DetectionModule module;
                using (var stream = moduleEntry.Open())
                using (var reader = new StreamReader(stream))
                {
                    var json = reader.ReadToEnd();
                    module = JsonSerializer.Deserialize<DetectionModule>(json, JsonOptions)!;
                }

                if (string.IsNullOrEmpty(module.Id)) return null;

                // Deduplicate module ID if it already exists
                var originalId = module.Id;
                var counter = 1;
                while (_state.Modules.ContainsKey(module.Id))
                {
                    module.Id = $"{originalId}-{counter++}";
                }

                var moduleDir = GetModulePath(module.Id);
                Directory.CreateDirectory(moduleDir);

                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.Equals("module.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // Directory entry

                    var destPath = Path.Combine(moduleDir, entry.FullName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                // Resolve relative paths to absolute for runtime use
                foreach (var image in module.Images.Values)
                {
                    if (!string.IsNullOrEmpty(image.FilePath) && !Path.IsPathRooted(image.FilePath))
                        image.FilePath = Path.Combine(moduleDir, image.FilePath);

                    if (!string.IsNullOrEmpty(image.Meter.RegionPreviewPath) && !Path.IsPathRooted(image.Meter.RegionPreviewPath))
                        image.Meter.RegionPreviewPath = Path.Combine(moduleDir, image.Meter.RegionPreviewPath);
                }

                _state.Modules[module.Id] = module;
                SaveConfig();

                return module;
            }
            catch
            {
                return null;
            }
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

            SaveModuleJsonFiles();

            ConfigurationChanged?.Invoke();
        }
        catch
        {
            // Failed to save configuration
        }
    }

    /// <summary>
    /// Writes a module.json inside each module folder with relative paths,
    /// keeping each module directory self-contained for sharing.
    /// </summary>
    private void SaveModuleJsonFiles()
    {
        foreach (var (moduleId, module) in _state.Modules)
        {
            try
            {
                var moduleDir = GetModulePath(moduleId);
                if (!Directory.Exists(moduleDir)) continue;

                var portable = JsonSerializer.Deserialize<DetectionModule>(
                    JsonSerializer.Serialize(module, JsonOptions), JsonOptions)!;

                foreach (var image in portable.Images.Values)
                {
                    if (!string.IsNullOrEmpty(image.FilePath))
                        image.FilePath = MakeRelativePath(moduleId, image.FilePath);
                    if (!string.IsNullOrEmpty(image.Meter.RegionPreviewPath))
                        image.Meter.RegionPreviewPath = MakeRelativePath(moduleId, image.Meter.RegionPreviewPath);
                }

                var moduleJson = JsonSerializer.Serialize(portable, JsonOptions);
                File.WriteAllText(Path.Combine(moduleDir, "module.json"), moduleJson);
            }
            catch
            {
                // Non-critical: module.json write failure doesn't block the main config
            }
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
                var legacyId = Path.GetFileNameWithoutExtension(imagePath);

                if (module2.Images.TryGetValue(imageId, out var existingByFileName))
                {
                    if (module2.Images.TryGetValue(legacyId, out var legacyDuplicate) && legacyId != imageId)
                    {
                        module2.Images.Remove(imageId);
                        module2.Images.Remove(legacyId);
                        legacyDuplicate.Id = imageId;
                        legacyDuplicate.FilePath = imagePath;
                        module2.Images[imageId] = legacyDuplicate;
                        continue;
                    }

                    existingByFileName.FilePath = imagePath;
                    continue;
                }

                if (module2.Images.TryGetValue(legacyId, out var legacyImage))
                {
                    module2.Images.Remove(legacyId);
                    legacyImage.Id = imageId;
                    legacyImage.FilePath = imagePath;
                    module2.Images[imageId] = legacyImage;
                    continue;
                }

                var existingByPath = module2.Images.Values.FirstOrDefault(i =>
                    string.Equals(
                        Path.GetFullPath(i.FilePath),
                        Path.GetFullPath(imagePath),
                        StringComparison.OrdinalIgnoreCase));

                if (existingByPath != null)
                {
                    existingByPath.FilePath = imagePath;
                    continue;
                }

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
        public HotkeyBinding PickerHotkey { get; set; } = new();
        public Dictionary<string, DetectionModule> Modules { get; set; } = [];
    }

    #endregion
}
