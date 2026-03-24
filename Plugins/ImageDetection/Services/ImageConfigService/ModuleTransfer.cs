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

                    if (!string.IsNullOrEmpty(image.Ocr.RegionPreviewPath))
                        image.Ocr.RegionPreviewPath = MakeRelativePath(moduleId, image.Ocr.RegionPreviewPath);
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
    /// Imports a module from a packaged archive, unpacked module folder,
    /// or legacy module folder/config file.
    /// </summary>
    public DetectionModule? ImportModule(string sourcePath)
    {
        lock (_lock)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                    return null;

                if (Directory.Exists(sourcePath))
                    return ImportModuleDirectory(sourcePath);

                if (!File.Exists(sourcePath))
                    return null;

                var fileName = Path.GetFileName(sourcePath);
                if (fileName.Equals("module.json", StringComparison.OrdinalIgnoreCase))
                    return ImportPortableModuleDirectory(Path.GetDirectoryName(sourcePath)!);

                if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                    return ImportLegacyModuleDirectory(Path.GetDirectoryName(sourcePath)!);

                return ImportModuleArchive(sourcePath);
            }
            catch
            {
                return null;
            }
        }
    }

    private DetectionModule? ImportModuleDirectory(string sourceDirectory)
    {
        var portableConfigPath = Path.Combine(sourceDirectory, "module.json");
        if (File.Exists(portableConfigPath))
            return ImportPortableModuleDirectory(sourceDirectory);

        var legacyConfigPath = Path.Combine(sourceDirectory, "config.json");
        if (File.Exists(legacyConfigPath))
            return ImportLegacyModuleDirectory(sourceDirectory);

        return null;
    }

    private DetectionModule? ImportModuleArchive(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var moduleEntry = zip.GetEntry("module.json");
        if (moduleEntry == null) return null;

        DetectionModule? module;
        using (var stream = moduleEntry.Open())
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            module = JsonSerializer.Deserialize<DetectionModule>(json, JsonOptions);
        }

        if (module == null) return null;

        PrepareImportedModuleIdentity(module, Path.GetFileNameWithoutExtension(archivePath));

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

        FinalizeImportedModule(module, moduleDir);
        return module;
    }

    private DetectionModule? ImportPortableModuleDirectory(string sourceDirectory)
    {
        var moduleJsonPath = Path.Combine(sourceDirectory, "module.json");
        if (!File.Exists(moduleJsonPath))
            return null;

        var json = File.ReadAllText(moduleJsonPath);
        var module = JsonSerializer.Deserialize<DetectionModule>(json, JsonOptions);
        if (module == null)
            return null;

        PrepareImportedModuleIdentity(module, Path.GetFileName(sourceDirectory));

        var moduleDir = GetModulePath(module.Id);
        Directory.CreateDirectory(moduleDir);

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            if (relativePath.Equals("module.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var destinationPath = Path.Combine(moduleDir, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (destinationDirectory != null)
                Directory.CreateDirectory(destinationDirectory);

            File.Copy(file, destinationPath, overwrite: true);
        }

        FinalizeImportedModule(module, moduleDir);
        return module;
    }

    private void PrepareImportedModuleIdentity(DetectionModule module, string fallbackName)
    {
        var requestedId = !string.IsNullOrWhiteSpace(module.Id)
            ? SanitizeId(module.Id)
            : SanitizeId(fallbackName);

        if (string.IsNullOrWhiteSpace(requestedId))
            requestedId = $"module-{DateTime.UtcNow:yyyyMMddHHmmss}";

        module.Id = GetUniqueModuleId(requestedId);

        if (string.IsNullOrWhiteSpace(module.Name))
            module.Name = fallbackName;

        if (module.CreatedAt == default)
            module.CreatedAt = DateTime.UtcNow;

        module.ModifiedAt = DateTime.UtcNow;
    }

    private void FinalizeImportedModule(DetectionModule module, string moduleDir)
    {
        foreach (var image in module.Images.Values)
        {
            if (!string.IsNullOrEmpty(image.FilePath) && !Path.IsPathRooted(image.FilePath))
                image.FilePath = Path.Combine(moduleDir, image.FilePath);

            if (!string.IsNullOrEmpty(image.Meter.RegionPreviewPath) && !Path.IsPathRooted(image.Meter.RegionPreviewPath))
                image.Meter.RegionPreviewPath = Path.Combine(moduleDir, image.Meter.RegionPreviewPath);

            if (!string.IsNullOrEmpty(image.Ocr.RegionPreviewPath) && !Path.IsPathRooted(image.Ocr.RegionPreviewPath))
                image.Ocr.RegionPreviewPath = Path.Combine(moduleDir, image.Ocr.RegionPreviewPath);

            if (image.CreatedAt == default)
                image.CreatedAt = DateTime.UtcNow;

            image.ModifiedAt = DateTime.UtcNow;
        }

        _state.Modules[module.Id] = module;
        SaveConfig();
    }

    private string GetUniqueModuleId(string baseId)
    {
        var moduleId = baseId;
        var counter = 1;
        while (_state.Modules.ContainsKey(moduleId))
        {
            moduleId = $"{baseId}-{counter++}";
        }

        return moduleId;
    }
}
