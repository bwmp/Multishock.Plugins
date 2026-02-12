using System.Text.Json;
using ImageDetection.Models;

namespace ImageDetection.Services;

public partial class ImageConfigService
{
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
                var newModule = new DetectionModule
                {
                    Id = moduleId,
                    Name = moduleId,
                    Enabled = true
                };
                _state.Modules[moduleId] = newModule;
            }

            var module = _state.Modules[moduleId];
            foreach (var imagePath in Directory.GetFiles(moduleDir, "*.png"))
            {
                var imageId = Path.GetFileName(imagePath);
                var legacyId = Path.GetFileNameWithoutExtension(imagePath);

                if (module.Images.TryGetValue(imageId, out var existingByFileName))
                {
                    if (module.Images.TryGetValue(legacyId, out var legacyDuplicate) && legacyId != imageId)
                    {
                        module.Images.Remove(imageId);
                        module.Images.Remove(legacyId);
                        legacyDuplicate.Id = imageId;
                        legacyDuplicate.FilePath = imagePath;
                        module.Images[imageId] = legacyDuplicate;
                        continue;
                    }

                    existingByFileName.FilePath = imagePath;
                    continue;
                }

                if (module.Images.TryGetValue(legacyId, out var legacyImage))
                {
                    module.Images.Remove(legacyId);
                    legacyImage.Id = imageId;
                    legacyImage.FilePath = imagePath;
                    module.Images[imageId] = legacyImage;
                    continue;
                }

                var existingByPath = module.Images.Values.FirstOrDefault(i =>
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
                module.Images[imageId] = image;
            }
        }
    }

    private class ConfigState
    {
        public CaptureConfig CaptureConfig { get; set; } = new();
        public string DefaultAlgorithmId { get; set; } = "template-matching";
        public bool BackgroundDetectionEnabled { get; set; }
        public bool DebugMode { get; set; }
        public HotkeyBinding PickerHotkey { get; set; } = new();
        public Dictionary<string, DetectionModule> Modules { get; set; } = [];
    }
}
