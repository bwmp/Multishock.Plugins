using System.IO.Compression;
using System.Text.Json;
using ImageDetection.Models;

namespace ImageDetection.Services;

public partial class ImageConfigService
{
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
                var moduleJsonEntry = zip.CreateEntry("module.json");
                using (var stream = moduleJsonEntry.Open())
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(moduleJson);
                }

                var moduleDir = GetModulePath(moduleId);
                if (Directory.Exists(moduleDir))
                {
                    foreach (var file in Directory.GetFiles(moduleDir, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(moduleDir, file);
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
                        continue;

                    var destPath = Path.Combine(moduleDir, entry.FullName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

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
}
