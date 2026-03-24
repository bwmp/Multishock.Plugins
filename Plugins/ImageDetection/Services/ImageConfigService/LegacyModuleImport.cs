using System.Text.Json;
using System.Text.Json.Serialization;
using ImageDetection.Models;

namespace ImageDetection.Services;

public partial class ImageConfigService
{
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private DetectionModule? ImportLegacyModuleDirectory(string sourceDirectory)
    {
        var configPath = Path.Combine(sourceDirectory, "config.json");
        if (!File.Exists(configPath))
            return null;

        var json = File.ReadAllText(configPath);
        var legacyModule = JsonSerializer.Deserialize<LegacyModuleConfig>(json, LegacyJsonOptions);
        if (legacyModule == null)
            return null;

        var folderName = Path.GetFileName(Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var moduleId = GetUniqueModuleId(SanitizeId(folderName));
        var moduleDir = GetModulePath(moduleId);
        Directory.CreateDirectory(moduleDir);

        var importedModule = new DetectionModule
        {
            Id = moduleId,
            Name = folderName,
            Description = BuildLegacyDescription(legacyModule),
            Enabled = legacyModule.Enabled,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        var importedTargets = new List<(LegacyImageConfig Legacy, DetectionImage Image)>();
        var resetLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (legacyImagePath, legacyImage) in legacyModule.Images)
        {
            var sourceImagePath = Path.Combine(sourceDirectory, legacyImagePath);
            if (!File.Exists(sourceImagePath))
                continue;

            var fileName = GetUniqueFileName(moduleDir, Path.GetFileName(legacyImagePath));
            var destinationPath = Path.Combine(moduleDir, fileName);
            File.Copy(sourceImagePath, destinationPath, overwrite: true);

            var image = CreateLegacyDetectionImage(legacyImagePath, destinationPath, legacyImage);
            importedModule.SetImage(image);
            importedTargets.Add((legacyImage, image));

            resetLookup[legacyImagePath] = image.Id;
            resetLookup[Path.GetFileName(legacyImagePath)] = image.Id;
            resetLookup[Path.GetFileNameWithoutExtension(legacyImagePath)] = image.Id;
        }

        if (importedModule.Images.Count == 0)
        {
            Directory.Delete(moduleDir, recursive: true);
            return null;
        }

        foreach (var (legacyImage, image) in importedTargets)
        {
            if (string.IsNullOrWhiteSpace(legacyImage.CooldownResetImage))
                continue;

            if (resetLookup.TryGetValue(legacyImage.CooldownResetImage, out var resetImageId))
                image.Cooldown.ResetImagePath = $"{moduleId}/{resetImageId}";
        }

        _state.Modules[moduleId] = importedModule;
        SaveConfig();
        return importedModule;
    }

    private DetectionImage CreateLegacyDetectionImage(string legacyImagePath, string destinationPath, LegacyImageConfig legacyImage)
    {
        var fileName = Path.GetFileName(destinationPath);
        var now = DateTime.UtcNow;

        return new DetectionImage
        {
            Id = fileName,
            Name = Path.GetFileNameWithoutExtension(fileName),
            FilePath = destinationPath,
            Enabled = legacyImage.Enabled,
            Threshold = Math.Clamp(legacyImage.Threshold, 0.0, 1.0),
            AlgorithmId = DefaultAlgorithmId,
            Region = CreateLegacyRegionConfig(legacyImage.Sections),
            Cooldown = new CooldownConfig
            {
                Type = MapLegacyCooldownType(legacyImage.CooldownType),
                DurationSeconds = Math.Max(0.0, legacyImage.Cooldown)
            },
            Action = new ActionConfig
            {
                Enabled = true,
                Type = MapLegacyActionType(legacyImage.ActionType),
                Intensity = Math.Clamp(legacyImage.Intensity, 0, 100),
                DurationSeconds = Math.Max(0.0, legacyImage.Duration),
                Mode = MapLegacyMode(legacyImage.Mode),
                RandomCountMin = 1,
                RandomCountMax = 1
            },
            CaptureResolution = CreateLegacyResolution(legacyImage.Resolution),
            AutoResize = true,
            TargetType = DetectionTargetType.Template,
            Notes = $"Imported from legacy module target '{legacyImagePath}'.",
            CreatedAt = now,
            ModifiedAt = now
        };
    }

    private static Resolution CreateLegacyResolution(int[]? resolution)
    {
        if (resolution is [var width, var height] && width > 0 && height > 0)
            return new Resolution(width, height);

        return new Resolution(1920, 1080);
    }

    private static RegionConfig CreateLegacyRegionConfig(bool[]? sections)
    {
        if (sections == null || sections.Length != 9)
            return new RegionConfig { Type = RegionType.FullScreen };

        var copiedSections = sections.ToArray();
        return new RegionConfig
        {
            Type = copiedSections.All(section => section) ? RegionType.FullScreen : RegionType.Grid,
            GridSections = new GridSections { Sections = copiedSections }
        };
    }

    private static ActionType MapLegacyActionType(LegacyEnumValue<int>? actionType)
    {
        return actionType?.Value switch
        {
            1 => ActionType.Vibrate,
            2 => ActionType.Beep,
            _ => actionType?.Name?.Contains("vibrate", StringComparison.OrdinalIgnoreCase) == true
                ? ActionType.Vibrate
                : actionType?.Name?.Contains("beep", StringComparison.OrdinalIgnoreCase) == true
                    ? ActionType.Beep
                    : ActionType.Shock
        };
    }

    private static ShockerMode MapLegacyMode(LegacyEnumValue<string>? mode)
    {
        if (string.Equals(mode?.Value, "random", StringComparison.OrdinalIgnoreCase)
            || mode?.Name?.Contains("random", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ShockerMode.Random;
        }

        return ShockerMode.Selected;
    }

    private static CooldownType MapLegacyCooldownType(LegacyEnumValue<int>? cooldownType)
    {
        return cooldownType?.Value switch
        {
            1 => CooldownType.Continuous,
            2 => CooldownType.ImageReset,
            _ => cooldownType?.Name?.Contains("continuous", StringComparison.OrdinalIgnoreCase) == true
                ? CooldownType.Continuous
                : cooldownType?.Name?.Contains("reset", StringComparison.OrdinalIgnoreCase) == true
                    ? CooldownType.ImageReset
                    : CooldownType.Standard
        };
    }

    private static string? BuildLegacyDescription(LegacyModuleConfig legacyModule)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(legacyModule.Description))
            parts.Add(legacyModule.Description.Trim());

        if (!string.IsNullOrWhiteSpace(legacyModule.Author))
            parts.Add($"Imported legacy author: {legacyModule.Author.Trim()}");

        if (parts.Count == 0)
            return "Imported from the legacy module format.";

        return string.Join(Environment.NewLine, parts);
    }

    private static string GetUniqueFileName(string directoryPath, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var counter = 1;

        while (File.Exists(Path.Combine(directoryPath, candidate)))
        {
            candidate = $"{name}_{counter++}{extension}";
        }

        return candidate;
    }

    private sealed class LegacyModuleConfig
    {
        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("images")]
        public Dictionary<string, LegacyImageConfig> Images { get; set; } = [];
    }

    private sealed class LegacyImageConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; } = 20;

        [JsonPropertyName("duration")]
        public double Duration { get; set; } = 1.0;

        [JsonPropertyName("action_type")]
        public LegacyEnumValue<int>? ActionType { get; set; }

        [JsonPropertyName("mode")]
        public LegacyEnumValue<string>? Mode { get; set; }

        [JsonPropertyName("cooldown")]
        public double Cooldown { get; set; } = 5.0;

        [JsonPropertyName("threshold")]
        public double Threshold { get; set; } = 0.8;

        [JsonPropertyName("cooldown_type")]
        public LegacyEnumValue<int>? CooldownType { get; set; }

        [JsonPropertyName("cooldown_reset_image")]
        public string? CooldownResetImage { get; set; }

        [JsonPropertyName("resolution")]
        public int[]? Resolution { get; set; }

        [JsonPropertyName("sections")]
        public bool[]? Sections { get; set; }
    }

    private sealed class LegacyEnumValue<T>
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public T? Value { get; set; }
    }
}
