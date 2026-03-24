using System.Text.Json;
using MultiShock.PluginSdk;

namespace SpeechToText.Services;

public class SpeechToTextSettingsService
{
    private const string ConfigFileName = "speech-to-text-config.json";

    private readonly string _configPath;
    private readonly string _pluginDataPath;

    private class ConfigState
    {
        public bool AutoStartListening { get; set; }

        public bool ListeningEnabled { get; set; }

        public string Culture { get; set; } = "en-US";

        public double MinimumConfidence { get; set; } = 0.1;

        public bool AutoRestartOnFailure { get; set; } = true;

        public int RestartDelayMs { get; set; } = 2000;

        public string WhisperModel { get; set; } = "TinyEn";

        public string WhisperModelPath { get; set; } = "";

        public bool DownloadModelIfMissing { get; set; } = true;

        public int SegmentDurationMs { get; set; } = 3500;

        public double SilenceEnergyThreshold { get; set; } = 0.0015;

        public bool UseDefaultMicrophone { get; set; } = true;

        public int MicrophoneDeviceNumber { get; set; } = 0;
    }

    private ConfigState _state = new();

    public SpeechToTextSettingsService(IPluginHost pluginHost)
    {
        _pluginDataPath = pluginHost.GetPluginDataPath(SpeechToTextPlugin.PluginId);
        _configPath = Path.Combine(_pluginDataPath, ConfigFileName);
        LoadConfig();
    }

    public string PluginDataPath => _pluginDataPath;

    public bool ListeningEnabled
        => _state.AutoStartListening;

    public bool AutoStartListening
    {
        get => _state.AutoStartListening;
        set
        {
            _state.AutoStartListening = value;
            SaveConfig();
        }
    }

    public string Culture
    {
        get => string.IsNullOrWhiteSpace(_state.Culture) ? "en-US" : _state.Culture;
        set
        {
            _state.Culture = string.IsNullOrWhiteSpace(value) ? "en-US" : value.Trim();
            SaveConfig();
        }
    }

    public double MinimumConfidence
    {
        get => Math.Clamp(_state.MinimumConfidence, 0.0, 1.0);
        set
        {
            _state.MinimumConfidence = Math.Clamp(value, 0.0, 1.0);
            SaveConfig();
        }
    }

    public bool AutoRestartOnFailure
    {
        get => _state.AutoRestartOnFailure;
        set
        {
            _state.AutoRestartOnFailure = value;
            SaveConfig();
        }
    }

    public int RestartDelayMs
    {
        get => Math.Clamp(_state.RestartDelayMs, 250, 30000);
        set
        {
            _state.RestartDelayMs = Math.Clamp(value, 250, 30000);
            SaveConfig();
        }
    }

    public string WhisperModel
    {
        get => string.IsNullOrWhiteSpace(_state.WhisperModel) ? "TinyEn" : _state.WhisperModel;
        set
        {
            _state.WhisperModel = string.IsNullOrWhiteSpace(value) ? "TinyEn" : value.Trim();
            SaveConfig();
        }
    }

    public string WhisperModelPath
    {
        get => _state.WhisperModelPath ?? string.Empty;
        set
        {
            _state.WhisperModelPath = value?.Trim() ?? string.Empty;
            SaveConfig();
        }
    }

    public bool DownloadModelIfMissing
    {
        get => _state.DownloadModelIfMissing;
        set
        {
            _state.DownloadModelIfMissing = value;
            SaveConfig();
        }
    }

    public int SegmentDurationMs
    {
        get => Math.Clamp(_state.SegmentDurationMs, 1000, 10000);
        set
        {
            _state.SegmentDurationMs = Math.Clamp(value, 1000, 10000);
            SaveConfig();
        }
    }

    public double SilenceEnergyThreshold
    {
        get => Math.Clamp(_state.SilenceEnergyThreshold, 0.0001, 0.05);
        set
        {
            _state.SilenceEnergyThreshold = Math.Clamp(value, 0.0001, 0.05);
            SaveConfig();
        }
    }

    public bool UseDefaultMicrophone
    {
        get => _state.UseDefaultMicrophone;
        set
        {
            _state.UseDefaultMicrophone = value;
            SaveConfig();
        }
    }

    public int MicrophoneDeviceNumber
    {
        get => Math.Max(0, _state.MicrophoneDeviceNumber);
        set
        {
            _state.MicrophoneDeviceNumber = Math.Max(0, value);
            SaveConfig();
        }
    }

    public void ApplySettings(Dictionary<string, object?> settings)
    {
        if (TryGetValue(settings, "listeningEnabled", out bool listeningEnabled))
        {
            _state.AutoStartListening = listeningEnabled;
        }

        if (TryGetValue(settings, "autoStartListening", out bool autoStartListening))
        {
            _state.AutoStartListening = autoStartListening;
        }

        if (TryGetValue(settings, "culture", out string? culture) && !string.IsNullOrWhiteSpace(culture))
        {
            _state.Culture = culture.Trim();
        }

        if (TryGetValue(settings, "minimumConfidence", out double minimumConfidence))
        {
            _state.MinimumConfidence = Math.Clamp(minimumConfidence, 0.0, 1.0);
        }

        if (TryGetValue(settings, "autoRestartOnFailure", out bool autoRestartOnFailure))
        {
            _state.AutoRestartOnFailure = autoRestartOnFailure;
        }

        if (TryGetValue(settings, "restartDelayMs", out int restartDelayMs))
        {
            _state.RestartDelayMs = Math.Clamp(restartDelayMs, 250, 30000);
        }

        if (TryGetValue(settings, "whisperModel", out string? whisperModel) && !string.IsNullOrWhiteSpace(whisperModel))
        {
            _state.WhisperModel = whisperModel.Trim();
        }

        if (TryGetValue(settings, "whisperModelPath", out string? whisperModelPath))
        {
            _state.WhisperModelPath = whisperModelPath?.Trim() ?? string.Empty;
        }

        if (TryGetValue(settings, "downloadModelIfMissing", out bool downloadModelIfMissing))
        {
            _state.DownloadModelIfMissing = downloadModelIfMissing;
        }

        if (TryGetValue(settings, "segmentDurationMs", out int segmentDurationMs))
        {
            _state.SegmentDurationMs = Math.Clamp(segmentDurationMs, 1000, 10000);
        }

        if (TryGetValue(settings, "silenceEnergyThreshold", out double silenceEnergyThreshold))
        {
            _state.SilenceEnergyThreshold = Math.Clamp(silenceEnergyThreshold, 0.0001, 0.05);
        }

        if (TryGetValue(settings, "useDefaultMicrophone", out bool useDefaultMicrophone))
        {
            _state.UseDefaultMicrophone = useDefaultMicrophone;
        }

        if (TryGetValue(settings, "microphoneDeviceNumber", out int microphoneDeviceNumber))
        {
            _state.MicrophoneDeviceNumber = Math.Max(0, microphoneDeviceNumber);
        }

        SaveConfig();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _state = JsonSerializer.Deserialize<ConfigState>(json) ?? new ConfigState();

                if (!_state.AutoStartListening && _state.ListeningEnabled)
                {
                    _state.AutoStartListening = true;
                }
            }
        }
        catch
        {
            _state = new ConfigState();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch
        {
        }
    }

    private static bool TryGetValue<T>(IReadOnlyDictionary<string, object?> settings, string key, out T value)
    {
        if (!settings.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            value = default!;
            return false;
        }

        try
        {
            if (rawValue is T typed)
            {
                value = typed;
                return true;
            }

            if (rawValue is JsonElement element)
            {
                value = element.Deserialize<T>()!;
                return value is not null;
            }

            value = (T)Convert.ChangeType(rawValue, typeof(T));
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }
}
