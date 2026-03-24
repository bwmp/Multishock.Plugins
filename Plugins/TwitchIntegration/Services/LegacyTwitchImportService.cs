using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchIntegration.Models;

namespace TwitchIntegration.Services;

public sealed class LegacyTwitchImportService
{
    private readonly CheerConfigService _cheerConfigService;
    private readonly RedeemConfigService _redeemConfigService;
    private readonly SubscriptionConfigService _subscriptionConfigService;
    private readonly FollowConfigService _followConfigService;
    private readonly HypeTrainConfigService _hypeTrainConfigService;

    public LegacyTwitchImportService(
        CheerConfigService cheerConfigService,
        RedeemConfigService redeemConfigService,
        SubscriptionConfigService subscriptionConfigService,
        FollowConfigService followConfigService,
        HypeTrainConfigService hypeTrainConfigService)
    {
        _cheerConfigService = cheerConfigService;
        _redeemConfigService = redeemConfigService;
        _subscriptionConfigService = subscriptionConfigService;
        _followConfigService = followConfigService;
        _hypeTrainConfigService = hypeTrainConfigService;
    }

    public Task<LegacyImportResult> ImportAsync(string configPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Task.FromResult(new LegacyImportResult(false, "Enter a config.json path first.", ""));
        }

        if (!File.Exists(configPath))
        {
            return Task.FromResult(new LegacyImportResult(false, $"Config not found: {configPath}", ""));
        }

        var json = File.ReadAllText(configPath);
        var legacy = JsonSerializer.Deserialize<LegacyRoot>(json);
        if (legacy == null)
        {
            return Task.FromResult(new LegacyImportResult(false, "Could not parse the legacy config.", ""));
        }

        var shockerLookup = BuildShockerLookup(legacy);

        var cheerConfig = BuildCheerConfig(legacy, shockerLookup);
        var redeemConfig = BuildRedeemConfig(legacy, shockerLookup);
        var subscriptionConfig = BuildSubscriptionConfig(legacy, shockerLookup);
        var followConfig = BuildFollowConfig(legacy, shockerLookup);
        var hypeTrainConfig = BuildHypeTrainConfig(legacy, shockerLookup);

        _cheerConfigService.ReplaceConfig(cheerConfig);
        _redeemConfigService.ReplaceConfig(redeemConfig);
        _subscriptionConfigService.ReplaceConfig(subscriptionConfig);
        _followConfigService.ReplaceConfig(followConfig);
        _hypeTrainConfigService.ReplaceConfig(hypeTrainConfig);

        var details = string.Join(Environment.NewLine, new[]
        {
            $"Cheers: {cheerConfig.Sections.Count} section(s)",
            $"Redeems: {redeemConfig.Redeems.Count} reward(s)",
            $"Subs: {subscriptionConfig.Sections.Count} tier section(s)",
            $"Follow: {(followConfig.Enabled ? "enabled" : "disabled")}",
            $"Hype Train: during {(hypeTrainConfig.During.Enabled ? "enabled" : "disabled")}, end {(hypeTrainConfig.End.Enabled ? "enabled" : "disabled")}"
        });

        return Task.FromResult(new LegacyImportResult(true, "Imported legacy Twitch settings into the new plugin config files.", details));
    }

    private static Dictionary<int, List<string>> BuildShockerLookup(LegacyRoot legacy)
    {
        var lookup = new Dictionary<int, List<string>>();

        foreach (var device in legacy.Devices ?? [])
        {
            foreach (var shocker in device.Shockers ?? [])
            {
                if (!lookup.TryGetValue(shocker.Identifier, out var ids))
                {
                    ids = new List<string>();
                    lookup[shocker.Identifier] = ids;
                }

                ids.Add($"{device.Id}:{shocker.Identifier}");
            }
        }

        return lookup;
    }

    private static CheerConfig BuildCheerConfig(LegacyRoot legacy, Dictionary<int, List<string>> shockerLookup)
    {
        var sections = (legacy.Cheers ?? [])
            .Select(cheer => new CheerSection
            {
                Name = cheer.IsDefault ? "Default" : cheer.Keyword,
                Keyword = cheer.IsDefault || string.Equals(cheer.Keyword, "Default", StringComparison.OrdinalIgnoreCase) ? "" : cheer.Keyword,
                Enabled = cheer.Enabled,
                CommandType = MapCommandType(cheer.ActionType),
                FixedAmount = cheer.FixedAmount,
                SelectedShockerIds = MapShockers(cheer.Shockers, shockerLookup),
                Brackets = (cheer.Brackets ?? [])
                    .Select(bracket => new CheerBracket
                    {
                        BitAmount = bracket.Bits,
                        Intensity = ClampIntensity(bracket.Intensity),
                        Duration = ClampDuration(bracket.Duration),
                        Mode = MapSelectionMode(bracket.Mode)
                    })
                    .OrderBy(b => b.BitAmount)
                    .ToList()
            })
            .ToList();

        return new CheerConfig
        {
            Enabled = sections.Any(s => s.Enabled),
            Sections = sections
        };
    }

    private static RedeemConfigRoot BuildRedeemConfig(LegacyRoot legacy, Dictionary<int, List<string>> shockerLookup)
    {
        var redeems = (legacy.Redeems ?? [])
            .Select(redeem => new RedeemConfig
            {
                RewardId = redeem.Identifier ?? string.Empty,
                RewardTitle = redeem.Name ?? string.Empty,
                Cost = 0,
                IsManageable = false,
                Enabled = redeem.Enabled,
                Intensity = ClampIntensity(redeem.Intensity),
                Duration = ClampDuration(redeem.Duration),
                Mode = MapSelectionMode(redeem.Mode),
                CommandType = MapCommandType(redeem.ActionType),
                SelectedShockerIds = MapShockers(redeem.Shockers, shockerLookup)
            })
            .ToList();

        return new RedeemConfigRoot
        {
            Enabled = redeems.Any(r => r.Enabled),
            Redeems = redeems
        };
    }

    private static SubscriptionConfig BuildSubscriptionConfig(LegacyRoot legacy, Dictionary<int, List<string>> shockerLookup)
    {
        var sections = (legacy.Subs?.Sections ?? [])
            .Select((section, index) =>
            {
                var tier = ParseTier(section.Name, index + 1);
                var bracketCommandType = section.Brackets?.FirstOrDefault()?.ActionType != null
                    ? MapCommandType(section.Brackets[0].ActionType)
                    : MapCommandType(section.ActionType);

                return new SubscriptionTierSection
                {
                    Tier = tier,
                    Name = string.IsNullOrWhiteSpace(section.Name) ? $"Tier {tier}" : section.Name,
                    Enabled = section.Enabled,
                    SelectedShockerIds = MapShockers(section.Shockers, shockerLookup),
                    Mode = MapSubscriptionMode(section.Type),
                    BracketCommandType = bracketCommandType,
                    FixedAmount = false,
                    Brackets = (section.Brackets ?? [])
                        .Select(bracket => new SubscriptionBracket
                        {
                            Count = Math.Max(bracket.Amount, 1),
                            Intensity = ClampIntensity(bracket.Intensity),
                            Duration = ClampDuration(bracket.Duration),
                            Mode = MapSelectionMode(bracket.Mode)
                        })
                        .OrderBy(b => b.Count)
                        .ToList(),
                    Incremental = new SubscriptionIncrementalAction
                    {
                        BaseIntensity = ClampIntensity(section.Intensity),
                        BaseDuration = ClampDuration(section.Duration),
                        IncrementIntensity = Math.Max(section.IncrementIntensity, 0),
                        IncrementDuration = Math.Max(section.IncrementDuration, 0),
                        MaxIntensity = ClampIntensity(section.MaxIntensity <= 0 ? 100 : section.MaxIntensity),
                        MaxDuration = ClampDuration(section.MaxDuration <= 0 ? 15.0 : section.MaxDuration),
                        CommandType = MapCommandType(section.ActionType),
                        Mode = MapSelectionMode(section.Mode)
                    }
                };
            })
            .OrderBy(section => section.Tier)
            .ToList();

        return new SubscriptionConfig
        {
            Enabled = sections.Any(section => section.Enabled),
            Sections = sections
        };
    }

    private static FollowConfig BuildFollowConfig(LegacyRoot legacy, Dictionary<int, List<string>> shockerLookup)
    {
        return new FollowConfig
        {
            Enabled = legacy.Follow?.Enabled ?? false,
            FetchFollowersOnStartup = true,
            Intensity = ClampIntensity(legacy.Follow?.Intensity ?? 30),
            Duration = ClampDuration(legacy.Follow?.Duration ?? 1.0),
            Mode = MapSelectionMode(legacy.Follow?.Mode),
            CommandType = MapCommandType(legacy.Follow?.ActionType),
            SelectedShockerIds = MapShockers(legacy.Follow?.Shockers, shockerLookup),
            KnownFollowerIds = new List<string>()
        };
    }

    private static HypeTrainConfig BuildHypeTrainConfig(LegacyRoot legacy, Dictionary<int, List<string>> shockerLookup)
    {
        return new HypeTrainConfig
        {
            Enabled = true,
            During = BuildHypeTrainAction(legacy.HypeTrain?.During, shockerLookup, HypeTrainScalingMode.Incremental),
            End = BuildHypeTrainAction(legacy.HypeTrain?.End, shockerLookup, HypeTrainScalingMode.Fixed)
        };
    }

    private static HypeTrainActionConfig BuildHypeTrainAction(LegacyHypeTrainAction? action, Dictionary<int, List<string>> shockerLookup, HypeTrainScalingMode fallbackScalingMode)
    {
        var maxIntensity = action?.MaxIntensity ?? 100;
        var maxDuration = action?.MaxDuration ?? 15.0;

        return new HypeTrainActionConfig
        {
            Enabled = action?.Enabled ?? false,
            SelectedShockerIds = MapShockers(action?.Shockers, shockerLookup),
            Mode = MapSelectionMode(action?.Mode),
            CommandType = MapCommandType(action?.ActionType),
            ScalingMode = MapHypeTrainScalingMode(action?.Type, fallbackScalingMode),
            Intensity = ClampIntensity(action?.Intensity ?? 50),
            Duration = ClampDuration(action?.Duration ?? 1.0),
            IncrementIntensity = Math.Max(action?.IncrementIntensity ?? 0, 0),
            IncrementDuration = Math.Max(action?.IncrementDuration ?? 0, 0),
            MaxIntensity = ClampIntensity(maxIntensity <= 0 ? 100 : maxIntensity),
            MaxDuration = ClampDuration(maxDuration <= 0 ? 15.0 : maxDuration)
        };
    }

    private static List<string> MapShockers(IEnumerable<LegacySelectedShocker>? shockers, Dictionary<int, List<string>> shockerLookup)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shocker in shockers ?? [])
        {
            if (shocker.Enabled == false) continue;
            if (!shockerLookup.TryGetValue(shocker.Identifier, out var combinedIds)) continue;

            foreach (var combinedId in combinedIds)
            {
                if (seen.Add(combinedId))
                {
                    result.Add(combinedId);
                }
            }
        }

        return result;
    }

    private static string MapCommandType(LegacyNamedValue? actionType)
    {
        if (actionType?.Value is 0) return "Shock";
        if (actionType?.Value is 1) return "Vibrate";
        if (actionType?.Value is 2) return "Beep";

        return actionType?.Name switch
        {
            "Shock" => "Shock",
            "Beep" => "Beep",
            _ => "Vibrate"
        };
    }

    private static SelectionMode MapSelectionMode(LegacyStringValue? mode)
    {
        return mode?.Value?.ToLowerInvariant() switch
        {
            "random" => SelectionMode.Random,
            "roundrobin" => SelectionMode.RoundRobin,
            "round_robin" => SelectionMode.RoundRobin,
            "round-robin" => SelectionMode.RoundRobin,
            _ => SelectionMode.All
        };
    }

    private static SubscriptionMode MapSubscriptionMode(LegacyStringValue? mode)
    {
        return mode?.Value?.ToLowerInvariant() switch
        {
            "incremental" => SubscriptionMode.Incremental,
            _ => SubscriptionMode.Brackets
        };
    }

    private static HypeTrainScalingMode MapHypeTrainScalingMode(LegacyStringValue? mode, HypeTrainScalingMode fallback)
    {
        return mode?.Value?.ToLowerInvariant() switch
        {
            "incremental" => HypeTrainScalingMode.Incremental,
            "fixed" => HypeTrainScalingMode.Fixed,
            _ => fallback
        };
    }

    private static int ParseTier(string? name, int fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        foreach (var ch in name)
        {
            if (char.IsDigit(ch) && int.TryParse(ch.ToString(), out var tier))
            {
                return tier;
            }
        }

        return fallback;
    }

    private static int ClampIntensity(int value) => Math.Clamp(value, 1, 100);

    private static double ClampDuration(double value) => Math.Clamp(value, 0.1, 15.0);

    private sealed class LegacyRoot
    {
        [JsonPropertyName("devices")]
        public List<LegacyDevice>? Devices { get; set; }

        [JsonPropertyName("cheers")]
        public List<LegacyCheer>? Cheers { get; set; }

        [JsonPropertyName("redeems")]
        public List<LegacyRedeem>? Redeems { get; set; }

        [JsonPropertyName("subs")]
        public LegacySubs? Subs { get; set; }

        [JsonPropertyName("follow")]
        public LegacyFollow? Follow { get; set; }

        [JsonPropertyName("hype_train")]
        public LegacyHypeTrain? HypeTrain { get; set; }
    }

    private sealed class LegacyDevice
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("shockers")]
        public List<LegacyDeviceShocker>? Shockers { get; set; }
    }

    private sealed class LegacyDeviceShocker
    {
        [JsonPropertyName("identifier")]
        public int Identifier { get; set; }
    }

    private sealed class LegacySelectedShocker
    {
        [JsonPropertyName("identifier")]
        public int Identifier { get; set; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }
    }

    private sealed class LegacyNamedValue
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public int? Value { get; set; }
    }

    private sealed class LegacyStringValue
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private sealed class LegacyCheer
    {
        [JsonPropertyName("keyword")]
        public string Keyword { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("fixed_amount")]
        public bool FixedAmount { get; set; }

        [JsonPropertyName("shockers")]
        public List<LegacySelectedShocker>? Shockers { get; set; }

        [JsonPropertyName("action_type")]
        public LegacyNamedValue? ActionType { get; set; }

        [JsonPropertyName("brackets")]
        public List<LegacyCheerBracket>? Brackets { get; set; }

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }
    }

    private sealed class LegacyCheerBracket
    {
        [JsonPropertyName("bits")]
        public int Bits { get; set; }

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("mode")]
        public LegacyStringValue? Mode { get; set; }
    }

    private sealed class LegacyRedeem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [JsonPropertyName("shockers")]
        public List<LegacySelectedShocker>? Shockers { get; set; }

        [JsonPropertyName("action_type")]
        public LegacyNamedValue? ActionType { get; set; }

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("mode")]
        public LegacyStringValue? Mode { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    private sealed class LegacySubs
    {
        [JsonPropertyName("sections")]
        public List<LegacySubSection>? Sections { get; set; }
    }

    private sealed class LegacySubSection
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("shockers")]
        public List<LegacySelectedShocker>? Shockers { get; set; }

        [JsonPropertyName("brackets")]
        public List<LegacySubBracket>? Brackets { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("type")]
        public LegacyStringValue? Type { get; set; }

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("mode")]
        public LegacyStringValue? Mode { get; set; }

        [JsonPropertyName("increment_intensity")]
        public int IncrementIntensity { get; set; }

        [JsonPropertyName("increment_duration")]
        public double IncrementDuration { get; set; }

        [JsonPropertyName("max_intensity")]
        public int MaxIntensity { get; set; }

        [JsonPropertyName("max_duration")]
        public double MaxDuration { get; set; }

        [JsonPropertyName("action_type")]
        public LegacyNamedValue? ActionType { get; set; }
    }

    private sealed class LegacySubBracket
    {
        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("mode")]
        public LegacyStringValue? Mode { get; set; }

        [JsonPropertyName("action_type")]
        public LegacyNamedValue? ActionType { get; set; }
    }

    private sealed class LegacyFollow
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("shockers")]
        public List<LegacySelectedShocker>? Shockers { get; set; }

        [JsonPropertyName("mode")]
        public LegacyStringValue? Mode { get; set; }

        [JsonPropertyName("action_type")]
        public LegacyNamedValue? ActionType { get; set; }
    }

    private sealed class LegacyHypeTrain
    {
        [JsonPropertyName("during")]
        public LegacyHypeTrainAction? During { get; set; }

        [JsonPropertyName("end")]
        public LegacyHypeTrainAction? End { get; set; }
    }

    private sealed class LegacyHypeTrainAction
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("increment_duration")]
        public double IncrementDuration { get; set; }

        [JsonPropertyName("max_duration")]
        public double MaxDuration { get; set; }

        [JsonPropertyName("intensity")]
        public int Intensity { get; set; }

        [JsonPropertyName("increment_intensity")]
        public int IncrementIntensity { get; set; }

        [JsonPropertyName("max_intensity")]
        public int MaxIntensity { get; set; }

        [JsonPropertyName("shockers")]
        public List<LegacySelectedShocker>? Shockers { get; set; }

        [JsonPropertyName("type")]
        public LegacyStringValue? Type { get; set; }

        [JsonPropertyName("mode")]
        public LegacyStringValue? Mode { get; set; }

        [JsonPropertyName("action_type")]
        public LegacyNamedValue? ActionType { get; set; }
    }
}

public sealed record LegacyImportResult(bool Success, string Message, string Details);
