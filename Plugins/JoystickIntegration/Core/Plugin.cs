using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using JoystickIntegration.Components.Config;
using JoystickIntegration.Nodes;
using JoystickIntegration.Services;

namespace JoystickIntegration;

public class JoystickIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    public static readonly string PluginId = "com.multishock.joystickintegration";

    public string Id => PluginId;

    public string Name => "Joystick.tv Integration";

    public string Version => BuildStamp.Version;

    public string Description => "Joystick.tv integration for tips, tip-menu items, follows, subscriptions, chat, wheel spins, and stream events";

    private JoystickConnectionService? _connectionService;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<JoystickSettingsService>();
        services.AddSingleton<JoystickAuthService>();
        services.AddSingleton<JoystickConnectionService>();
        services.AddSingleton<DeviceActionRunner>();
        services.AddSingleton<JoystickTriggerManager>();
        services.AddSingleton<TipRulesConfigService>();
        services.AddSingleton<TipBracketsConfigService>();
        services.AddSingleton<EventsConfigService>();
        services.AddSingleton<ChatFeedbackService>();
        services.AddSingleton<EventLogService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        // Logger category must include "Plugin" for proper log routing
        _logger = host?.CreateLogger("JoystickIntegration.Plugin");
        _logger?.LogInformation("Joystick.tv Integration plugin initialized");

        _connectionService = sp.GetService(typeof(JoystickConnectionService)) as JoystickConnectionService;

        // Instantiate services so they subscribe to connection events in their constructors.
        // Order matters: rules before brackets (brackets depends on rules for override checks).
        _ = sp.GetService(typeof(JoystickTriggerManager)) as JoystickTriggerManager;
        _ = sp.GetService(typeof(TipRulesConfigService)) as TipRulesConfigService;
        _ = sp.GetService(typeof(TipBracketsConfigService)) as TipBracketsConfigService;
        _ = sp.GetService(typeof(EventsConfigService)) as EventsConfigService;
        _ = sp.GetService(typeof(ChatFeedbackService)) as ChatFeedbackService;
        _ = sp.GetService(typeof(EventLogService)) as EventLogService;

        // Auto-connect if enabled and credentials are stored
        var settings = sp.GetService(typeof(JoystickSettingsService)) as JoystickSettingsService;
        if (_connectionService != null && settings != null &&
            settings.AutoConnect && settings.HasCredentials && !_connectionService.IsConnected)
        {
            _ = _connectionService.ConnectAsync();
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // Settings for this plugin are managed on the plugin's own page
    }

    // ========== STYLES (IPluginWithStyles) ==========

    private static readonly Lazy<string> _stylesheet = new(() =>
    {
        using var stream = typeof(JoystickIntegrationPlugin).Assembly
            .GetManifestResourceStream("JoystickIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;

    public string? GetStylesheetId() => "joystickintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Joystick.tv",
            Href = "/joystickintegration",
            Icon = "gamepad",
            Order = 60
        },
        new NavigationItem
        {
            Text = "Tip Brackets",
            Href = "/tip-brackets",
            Icon = "coins",
            Order = 61
        },
        new NavigationItem
        {
            Text = "Tip Menu Items",
            Href = "/tip-menu-rules",
            Icon = "list",
            Order = 62
        },
        new NavigationItem
        {
            Text = "Follows / Subs / More",
            Href = "/events-config",
            Icon = "heart",
            Order = 63
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new TipTriggerNode();
        yield return new TipMenuTriggerNode();
        yield return new TipGoalTriggerNode();
        yield return new FollowTriggerNode();
        yield return new SubscribeTriggerNode();
        yield return new WheelSpinTriggerNode();
        yield return new ChatTriggerNode();
        yield return new EnterTriggerNode();
        yield return new LeaveTriggerNode();
        yield return new StreamStateTriggerNode();
    }
}
