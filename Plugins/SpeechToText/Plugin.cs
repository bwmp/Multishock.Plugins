using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using SpeechToText.Components.Config;
using SpeechToText.Nodes;
using SpeechToText.Services;

namespace SpeechToText;

/// <summary>
/// Continuous speech recognition with sentence and word triggers.
/// </summary>
public class SpeechToTextPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    public const string PluginId = "com.multishock.speechtotext";

    public string Id => PluginId;

    public string Name => "SpeechToText";

    public string Version => BuildStamp.Version;

    public string Description => "Continuous speech recognition with sentence and word triggers.";

    private SpeechToTextSettingsService? _settingsService;
    private SpeechRecognitionService? _recognitionService;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SpeechToTextSettingsService>();
        services.AddSingleton<SpeechRecognitionService>();
        services.AddSingleton<SpeechToTextTriggerManager>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        _logger = host?.CreateLogger("SpeechToText.Plugin");
        _logger?.LogInformation("SpeechToText plugin initialized");

        _settingsService = sp.GetService(typeof(SpeechToTextSettingsService)) as SpeechToTextSettingsService;
        _recognitionService = sp.GetService(typeof(SpeechRecognitionService)) as SpeechRecognitionService;

        _ = sp.GetService(typeof(SpeechToTextTriggerManager)) as SpeechToTextTriggerManager;

        if (_settingsService != null &&
            _recognitionService != null &&
            _settingsService.AutoStartListening)
        {
            _ = _recognitionService.StartListeningAsync();
        }
    }

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["autoStartListening"] = false,
        ["culture"] = "en-US",
        ["minimumConfidence"] = 0.1,
        ["autoRestartOnFailure"] = true,
        ["restartDelayMs"] = 2000,
        ["whisperModel"] = "TinyEn",
        ["whisperModelPath"] = "",
        ["downloadModelIfMissing"] = true,
        ["segmentDurationMs"] = 3500,
        ["silenceEnergyThreshold"] = 0.0015,
        ["useDefaultMicrophone"] = true,
        ["microphoneDeviceNumber"] = 0,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        if (_settingsService == null || _recognitionService == null)
        {
            return;
        }

        _settingsService.ApplySettings(settings);
        _ = ApplyRuntimeConfigurationAsync();
    }

    private async Task ApplyRuntimeConfigurationAsync()
    {
        if (_settingsService == null || _recognitionService == null)
        {
            return;
        }

        if (_settingsService.AutoStartListening)
        {
            await _recognitionService.StartListeningAsync();
        }
    }

    public string GetStylesheet() => "";

    public string? GetStylesheetId() => "speechtotext-styles";

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "SpeechToText",
            Href = "/speechtotext",
            Icon = "mic",
            Order = 50
        }
    ];

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new SentenceRecognizedTriggerNode();
        yield return new WordRecognizedTriggerNode();
    }
}
