using System.Text.Json;
using System.Text.Json.Serialization;
using ImageDetection.Models;
using MultiShock.PluginSdk;

namespace ImageDetection.Services;

/// <summary>
/// Service for managing image detection configuration and persistence.
/// </summary>
public partial class ImageConfigService
{
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

        _dataPath = _pluginHost.GetPluginDataPath(ImageDetectionPlugin.PluginId);
        _configPath = Path.Combine(_dataPath, ConfigFileName);
        _modulesPath = Path.Combine(_dataPath, ModulesFolderName);

        Directory.CreateDirectory(_dataPath);
        Directory.CreateDirectory(_modulesPath);

        LoadConfig();
    }

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
}
