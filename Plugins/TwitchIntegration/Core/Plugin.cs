using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using TwitchIntegration.Nodes;
using TwitchIntegration.Components.Config;
using TwitchIntegration.Services;

namespace TwitchIntegration;

public class TwitchIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider, IPluginGuidedSetupProvider
{
    // ========== PLUGIN METADATA ==========

    public static readonly string PluginId = "com.multishock.twitchintegration";

    public string Id => PluginId;

    public string Name => "Twitch Integration";

    public string Version => BuildStamp.Version;

    public string Description => "Twitch EventSub integration for channel events like cheers, subs, follows, raids, and more";

    private TwitchEventSubService? _eventSubService;
    private static ILogger? _logger;

    // Expose logger to services and nodes
    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TwitchEventSubService>();
        services.AddSingleton<TwitchTriggerManager>();
        services.AddSingleton<TwitchAuthService>();
        services.AddSingleton<CheerConfigService>();
        services.AddSingleton<SubscriptionConfigService>();
        services.AddSingleton<FollowConfigService>();
        services.AddSingleton<RedeemConfigService>();
        services.AddSingleton<HypeTrainConfigService>();
        services.AddSingleton<LegacyTwitchImportService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        var actions = sp.GetService(typeof(IDeviceActions)) as IDeviceActions;
        
        // Initialize logger (must include "Plugin" for proper log routing)
        _logger = host?.CreateLogger("TwitchIntegration.Plugin");
        _logger?.LogInformation("Twitch Integration plugin initialized");

        _eventSubService = sp.GetService(typeof(TwitchEventSubService)) as TwitchEventSubService;

        // Initialize the trigger manager (it subscribes to events in constructor)
        _ = sp.GetService(typeof(TwitchTriggerManager)) as TwitchTriggerManager;

        // Initialize the cheer config service (subscribes to OnCheer in constructor)
        _ = sp.GetService(typeof(CheerConfigService)) as CheerConfigService;

        // Initialize the subscription config service (subscribes to sub events in constructor)
        _ = sp.GetService(typeof(SubscriptionConfigService)) as SubscriptionConfigService;

        // Initialize the follow config service (subscribes to follow events in constructor)
        _ = sp.GetService(typeof(FollowConfigService)) as FollowConfigService;

        // Initialize the redeem config service (subscribes to channel point redemption events in constructor)
        _ = sp.GetService(typeof(RedeemConfigService)) as RedeemConfigService;

        // Initialize the hype train config service (subscribes to hype train events in constructor)
        _ = sp.GetService(typeof(HypeTrainConfigService)) as HypeTrainConfigService;

        // Attempt auto-connect if a stored token exists and auto-connect is enabled
        var authService = sp.GetService(typeof(TwitchAuthService)) as TwitchAuthService;
        if (_eventSubService != null && authService != null &&
            authService.AutoConnect &&
            !string.IsNullOrEmpty(authService.StoredToken) &&
            !_eventSubService.IsConnected)
        {
            _ = _eventSubService.ConnectAsync(authService.StoredToken!);
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
        ["autoConnect"] = false,
        ["oauthToken"] = "",
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // Auto-connect if enabled and token is set
        if (_eventSubService != null &&
            settings.TryGetValue("autoConnect", out var autoConnect) && autoConnect is true &&
            settings.TryGetValue("oauthToken", out var token) && token is string oauthToken &&
            !string.IsNullOrEmpty(oauthToken) &&
            !_eventSubService.IsConnected)
        {
            _ = _eventSubService.ConnectAsync(oauthToken);
        }
    }

    // ========== STYLES (IPluginWithStyles) ==========

    private static readonly Lazy<string> _stylesheet = new(() =>
    {
        using var stream = typeof(TwitchIntegrationPlugin).Assembly
            .GetManifestResourceStream("TwitchIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;

    public string? GetStylesheetId() => "twitchintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Twitch Integration",
            Href = "/twitchintegration",
            Icon = "plug",
            Order = 50
        },
        new NavigationItem
        {
            Text = "Bits/Cheers",
            Href = "/cheer-config",
            Icon = "coins",
            Order = 51
        },
        new NavigationItem
        {
            Text = "Subs / Gifted Subs",
            Href = "/sub-config",
            Icon = "user-plus",
            Order = 52
        },
        new NavigationItem
        {
            Text = "Follows",
            Href = "/follow-config",
            Icon = "heart",
            Order = 53
        },
        new NavigationItem
        {
            Text = "Hype Trains",
            Href = "/hype-train-config",
            Icon = "zap",
            Order = 54
        },
        new NavigationItem
        {
            Text = "Channel Points",
            Href = "/redeem-config",
            Icon = "gift",
            Order = 55
        }
    ];

    public IEnumerable<GuidedSetupDefinition> GetGuidedSetups() =>
    [
        new GuidedSetupDefinition(
            Id: "cheers-and-subs",
            Title: "Twitch Cheers + Subs Setup",
            Description: "Connect Twitch, map cheers to brackets, and configure how subscription events trigger actions.",
            StartRoute: "/twitchintegration",
            Order: 0,
            Steps:
            [
                new GuidedSetupStep(
                    Id: "twitch-home-connection",
                    Title: "Connect your Twitch account first",
                    Description: "This connection card is the starting point for the whole plugin. Login with Twitch or connect with a saved token so MultiShock can subscribe to EventSub events like cheers, subscriptions, follows, and channel points. Nothing in the cheer or sub pages will fire until the plugin can connect successfully.",
                    TargetSelector: "[data-guided-setup='twitch-home-connection']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-home-login-button",
                    Title: "Use the Twitch login button for the easiest setup",
                    Description: "This button handles the normal authorization flow and requests the scopes the plugin needs. If you already have a valid token, the same area can reconnect with it instead. Manual token entry is still available lower in the connection card, but the guided login path is usually the least painful option.",
                    TargetSelector: "[data-guided-setup='twitch-home-login-button']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-home-auto-connect",
                    Title: "Decide how reconnects should work",
                    Description: "Auto-connect makes the plugin reconnect on app startup so you do not have to babysit it every time MultiShock launches. The local CLI toggle is mostly for testing and troubleshooting, so most users can leave that off unless they know they need it.",
                    TargetSelector: "[data-guided-setup='twitch-home-auto-connect']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-home-open-cheers",
                    Title: "Next, open the cheers configuration",
                    Description: "Use this quick link to jump straight into Bits/Cheers setup. That page lets you define keyword-based sections and bit brackets so different cheers can trigger different actions and intensities.",
                    TargetSelector: "[data-guided-setup='twitch-home-open-cheers']",
                    NextMode: GuidedSetupNextMode.WaitForRoute,
                    ExpectedRoute: "/cheer-config"),
                new GuidedSetupStep(
                    Id: "twitch-cheers-toolbar",
                    Title: "Enable cheers and add sections here",
                    Description: "The master toggle turns all cheer handling on or off without deleting your work. Add Section creates a new matching rule. A good pattern is one default section as a fallback plus optional keyword sections for themed commands like shock, buzz, or celebration.",
                    TargetSelector: "[data-guided-setup='twitch-cheers-toolbar']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-cheers-sections",
                    Title: "Sections decide which cheer rule is used",
                    Description: "Each section is a matching bucket. The plugin checks the cheer message and bit amount, finds the best section, then chooses a bracket inside that section. Put broad fallback behavior in the default section and reserve keyword sections for more specific reactions so your rules stay predictable.",
                    TargetSelector: "[data-guided-setup='twitch-cheers-sections']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-cheers-editor",
                    Title: "Map cheer sections to the right shockers and action type",
                    Description: "Inside a section, give it a clear name, optionally set a keyword, choose the command type, and pick the shockers that should react. If the keyword is blank, the section behaves like a default catch-all. This is the main place where you decide what kind of device response a matching cheer should produce.",
                    TargetSelector: "[data-guided-setup='twitch-cheers-editor']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-cheers-brackets",
                    Title: "Bit brackets translate cheer size into output strength",
                    Description: "Brackets let larger cheers do more than smaller ones. You can define thresholds like 100, 500, and 1000 bits with different intensity, duration, and selection behavior. Round mode makes the closest lower bracket win, while exact mode only fires when the bit amount matches exactly.",
                    TargetSelector: "[data-guided-setup='twitch-cheers-brackets']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-cheers-open-subs",
                    Title: "Now open subscriptions setup",
                    Description: "Use this quick link to move into the subscription page. That page handles regular subs, resubs, and gifted subs, and it can either use bracket thresholds or scale effects incrementally as the sub count increases.",
                    TargetSelector: "[data-guided-setup='twitch-cheers-open-subs']",
                    NextMode: GuidedSetupNextMode.WaitForRoute,
                    ExpectedRoute: "/sub-config"),
                new GuidedSetupStep(
                    Id: "twitch-subs-sections",
                    Title: "Subscription setup is organized by tier",
                    Description: "Each tier has its own section so you can make Tier 1, Tier 2, and Tier 3 behave differently. Gifted subs and bulk gifts also flow through the matching tier, so this is where you decide which shockers react for each tier before choosing how scaling works.",
                    TargetSelector: "[data-guided-setup='twitch-subs-sections']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-subs-mode",
                    Title: "Choose between bracket-based and incremental scaling",
                    Description: "Brackets are best when you want hand-tuned milestones like 1, 5, or 10 gifted subs. Incremental mode is better when you want the same pattern to scale smoothly as the sub count grows. Pick the mode that matches how predictable or dynamic you want gifted-sub reactions to feel.",
                    TargetSelector: "[data-guided-setup='twitch-subs-mode']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-subs-brackets",
                    Title: "Bracket mode can be built from scratch or converted from cheers",
                    Description: "In bracket mode, you can define sub-count thresholds with their own intensity and duration, or import an existing cheer section and convert its brackets into a subscription rule. That is useful when you want cheers and subs to share a similar progression without re-entering every threshold by hand.",
                    TargetSelector: "[data-guided-setup='twitch-subs-brackets']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-subs-incremental",
                    Title: "Incremental mode scales from base values up to a cap",
                    Description: "These controls define the base intensity and duration for a single sub, how much each extra sub adds, and the maximum limit the effect can grow to. It is ideal for gift bombs because a 20-sub burst can feel meaningfully bigger than a single sub without you having to create a long list of manual brackets.",
                    TargetSelector: "[data-guided-setup='twitch-subs-incremental']",
                    NextMode: GuidedSetupNextMode.Manual),
                new GuidedSetupStep(
                    Id: "twitch-subs-help",
                    Title: "Keep this behavior reference handy",
                    Description: "This help card summarizes how the page interprets subscription events. If something ever behaves differently than expected, come back here first to confirm whether the current section is using bracket matching or incremental scaling, because that decision changes how gift counts are translated into actions.",
                    TargetSelector: "[data-guided-setup='twitch-subs-help']",
                    NextMode: GuidedSetupNextMode.Manual)
            ])
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        // Twitch event trigger nodes
        yield return new CheerTriggerNode();
        yield return new CheerBracketTriggerNode();
        yield return new SubscribeTriggerNode();
        yield return new SubscriptionGiftTriggerNode();
        yield return new SubscriptionMessageTriggerNode();
        yield return new SubscriptionBracketTriggerNode();
        yield return new FollowTriggerNode();
        yield return new HypeTrainBeginTriggerNode();
        yield return new HypeTrainProgressTriggerNode();
        yield return new HypeTrainEndTriggerNode();
        yield return new RaidTriggerNode();
        yield return new ChannelPointRedemptionTriggerNode();
        yield return new ChatMessageTriggerNode();
        
        // Twitch action nodes
        yield return new SendChatMessageNode();
        yield return new SetRedeemEnabledNode();
        yield return new SetConfigRedeemEnabledNode();
    }
}
