using System.Text.Json;
using MultiShock.PluginSdk;
using TwitchIntegrationPlugin.Models;

namespace TwitchIntegrationPlugin.Services;

public class SubscriptionConfigService
{
    private const string ConfigFileName = "subscription-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly IDeviceActions _deviceActions;
    private readonly TwitchEventSubService _eventSubService;
    private readonly string _configPath;

    // Round-robin state: tracks current index per section
    private readonly Dictionary<string, int> _roundRobinIndices = new();
    private readonly object _roundRobinLock = new();

    private SubscriptionConfig _config = new();

    public event Action? ConfigChanged;

    public SubscriptionConfig Config => _config;

    public SubscriptionConfigService(
        IPluginHost pluginHost,
        IDeviceActions deviceActions,
        TwitchEventSubService eventSubService)
    {
        _pluginHost = pluginHost;
        _deviceActions = deviceActions;
        _eventSubService = eventSubService;

        var dataPath = _pluginHost.GetPluginDataPath("com.multishock.twitchintegration");
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        // Subscribe to Twitch events
        _eventSubService.OnSubscribe += HandleSubscribeEvent;
        _eventSubService.OnSubscriptionGift += HandleSubscriptionGiftEvent;
        _eventSubService.OnSubscriptionMessage += HandleSubscriptionMessageEvent;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<SubscriptionConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SubscriptionConfigService] Failed to load config: {ex.Message}");
            _config = CreateDefaultConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SubscriptionConfigService] Failed to save config: {ex.Message}");
        }
    }

    private SubscriptionConfig CreateDefaultConfig()
    {
        return new SubscriptionConfig
        {
            Enabled = true,
            Sections = new List<SubscriptionTierSection>
            {
                CreateDefaultSection(1),
                CreateDefaultSection(2),
                CreateDefaultSection(3)
            }
        };
    }

    private static SubscriptionTierSection CreateDefaultSection(int tier)
    {
        var name = tier switch
        {
            1 => "Tier 1",
            2 => "Tier 2",
            3 => "Tier 3",
            _ => $"Tier {tier}"
        };

        return new SubscriptionTierSection
        {
            Tier = tier,
            Name = name,
            Enabled = true,
            Mode = SubscriptionMode.Brackets,
            BracketCommandType = "Vibrate",
            FixedAmount = false,
            Brackets = new List<SubscriptionBracket>
            {
                new SubscriptionBracket { Count = 1, Intensity = 30, Duration = 1.0, Mode = SelectionMode.All },
                new SubscriptionBracket { Count = 5, Intensity = 60, Duration = 2.0, Mode = SelectionMode.All },
                new SubscriptionBracket { Count = 10, Intensity = 80, Duration = 3.0, Mode = SelectionMode.All },
            },
            Incremental = new SubscriptionIncrementalAction
            {
                BaseIntensity = 50,
                BaseDuration = 1.0,
                MaxIntensity = 100,
                MaxDuration = 5.0,
                IncrementIntensity = 5,
                IncrementDuration = 0.5,
                CommandType = "Vibrate",
                Mode = SelectionMode.All
            }
        };
    }

    // ===== Event Handling =====

    private void HandleSubscribeEvent(SubscribeEvent subscribeEvent)
    {
        if (!_config.Enabled) return;

        var section = FindSectionForTier(subscribeEvent.TierNumber);
        if (section == null || !section.Enabled) return;
        var count = 1;

        if (section.Mode == SubscriptionMode.Brackets)
        {
            ExecuteBracketAction(section, count);
        }
        else
        {
            ExecuteIncrementalAction(section, count);
        }
    }

    private void HandleSubscriptionMessageEvent(SubscriptionMessageEvent messageEvent)
    {
        if (!_config.Enabled) return;

        var section = FindSectionForTier(messageEvent.TierNumber);
        if (section == null || !section.Enabled) return;
        var count = 1;

        if (section.Mode == SubscriptionMode.Brackets)
        {
            ExecuteBracketAction(section, count);
        }
        else
        {
            ExecuteIncrementalAction(section, count);
        }
    }

    private void HandleSubscriptionGiftEvent(SubscriptionGiftEvent giftEvent)
    {
        if (!_config.Enabled) return;

        var section = FindSectionForTier(giftEvent.TierNumber);
        if (section == null || !section.Enabled) return;
        var count = giftEvent.Total;

        if (section.Mode == SubscriptionMode.Brackets)
        {
            ExecuteBracketAction(section, count);
        }
        else
        {
            ExecuteIncrementalAction(section, count);
        }
    }

    private SubscriptionTierSection? FindSectionForTier(int tier)
    {
        return _config.Sections.FirstOrDefault(s => s.Enabled && s.Tier == tier);
    }

    private void ExecuteIncrementalAction(SubscriptionTierSection section, int count)
    {
        if (!section.SelectedShockerIds.Any()) return;
        if (count <= 0) return;

        var cfg = section.Incremental;

        // Base + (count - 1) * increment, clamped to max values
        var rawIntensity = cfg.BaseIntensity + (count - 1) * cfg.IncrementIntensity;
        var rawDuration = cfg.BaseDuration + (count - 1) * cfg.IncrementDuration;

        var intensity = Math.Clamp(rawIntensity, 1, Math.Clamp(cfg.MaxIntensity, 1, 100));
        var duration = Math.Clamp(rawDuration, 0.1, Math.Clamp(cfg.MaxDuration, 0.1, 15.0));

        ExecuteDeviceAction(section, intensity, duration, cfg.CommandType, cfg.Mode);
    }

    private void ExecuteBracketAction(SubscriptionTierSection section, int count)
    {
        if (!section.SelectedShockerIds.Any()) return;

        var bracket = FindMatchingBracket(section, count);
        if (bracket == null) return;

        var intensity = Math.Clamp(bracket.Intensity, 1, 100);
        var duration = Math.Clamp(bracket.Duration, 0.1, 15.0);

        ExecuteDeviceAction(section, intensity, duration, section.BracketCommandType, bracket.Mode);
    }

    private SubscriptionBracket? FindMatchingBracket(SubscriptionTierSection section, int count)
    {
        if (!section.Brackets.Any()) return null;

        var sortedBrackets = section.Brackets
            .OrderByDescending(b => b.Count)
            .ToList();

        if (section.FixedAmount)
        {
            return sortedBrackets.FirstOrDefault(b => b.Count == count);
        }
        else
        {
            return sortedBrackets.FirstOrDefault(b => b.Count <= count);
        }
    }

    private void ExecuteDeviceAction(SubscriptionTierSection section, int intensity, double duration, string commandTypeString, SelectionMode mode)
    {
        var commandType = commandTypeString switch
        {
            "Shock" => CommandType.Shock,
            "Vibrate" => CommandType.Vibrate,
            "Beep" => CommandType.Beep,
            _ => CommandType.Vibrate
        };

        var shockerIds = GetShockerIdsForExecution(section, mode);
        if (!shockerIds.Any()) return;

        var parsedIds = shockerIds
            .Select(id => id.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
            .ToList();

        if (!parsedIds.Any()) return;

        var deviceIds = parsedIds.Select(p => p.deviceId).Distinct();
        var shockerIdInts = parsedIds.Select(p => p.shockerId);

        _deviceActions.PerformAction(
            intensity: intensity,
            durationSeconds: duration,
            command: commandType,
            deviceIds: deviceIds,
            shockerIds: shockerIdInts
        );
    }

    private List<string> GetShockerIdsForExecution(SubscriptionTierSection section, SelectionMode mode)
    {
        var availableIds = section.SelectedShockerIds
            .Where(id => IsShockerLoaded(id))
            .ToList();

        if (!availableIds.Any()) return new List<string>();

        return mode switch
        {
            SelectionMode.All => availableIds,
            SelectionMode.Random => new List<string> { availableIds[Random.Shared.Next(availableIds.Count)] },
            SelectionMode.RoundRobin => new List<string> { GetNextRoundRobinShocker(section.Id, availableIds) },
            _ => availableIds
        };
    }

    private string GetNextRoundRobinShocker(string sectionId, List<string> availableIds)
    {
        lock (_roundRobinLock)
        {
            if (!_roundRobinIndices.TryGetValue(sectionId, out var currentIndex))
            {
                currentIndex = 0;
            }

            currentIndex = currentIndex % availableIds.Count;
            var result = availableIds[currentIndex];

            _roundRobinIndices[sectionId] = (currentIndex + 1) % availableIds.Count;

            return result;
        }
    }

    private bool IsShockerLoaded(string combinedId)
    {
        var parts = combinedId.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var deviceId)) return false;
        if (!int.TryParse(parts[1], out var shockerId)) return false;

        return _deviceActions.IsShockerLoaded(deviceId, shockerId);
    }

    #region CRUD Operations for UI

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        SaveConfig();
    }

    public void UpdateSection(SubscriptionTierSection section)
    {
        var index = _config.Sections.FindIndex(s => s.Id == section.Id);
        if (index >= 0)
        {
            _config.Sections[index] = section;
            SaveConfig();
        }
    }

    #endregion
}
