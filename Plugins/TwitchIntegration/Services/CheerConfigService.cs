using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using TwitchIntegration.Models;

namespace TwitchIntegration.Services;

public class CheerConfigService
{
    private const string ConfigFileName = "cheer-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly IDeviceActions _deviceActions;
    private readonly TwitchEventSubService _eventSubService;
    private readonly TwitchTriggerManager _triggerManager;
    private readonly string _configPath;

    // Round-robin state: tracks current index per section
    private readonly Dictionary<string, int> _roundRobinIndices = [];
    private readonly object _roundRobinLock = new();

    private CheerConfig _config = new();

    public event Action? ConfigChanged;

    public CheerConfig Config => _config;

    public CheerConfigService(
        IPluginHost pluginHost,
        IDeviceActions deviceActions,
        TwitchEventSubService eventSubService,
        TwitchTriggerManager triggerManager)
    {
        _pluginHost = pluginHost;
        _deviceActions = deviceActions;
        _eventSubService = eventSubService;
        _triggerManager = triggerManager;

        var dataPath = _pluginHost.GetPluginDataPath("com.multishock.twitchintegration");
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _eventSubService.OnCheer += HandleCheerEvent;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<CheerConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TwitchIntegration.Logger?.LogError(ex, "Failed to load cheer config");
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
            TwitchIntegration.Logger?.LogError(ex, "Failed to save cheer config");
        }
    }

    private CheerConfig CreateDefaultConfig()
    {
        return new CheerConfig
        {
            Enabled = true,
            Sections = new List<CheerSection>
            {
                new CheerSection
                {
                    Name = "Default",
                    Keyword = "",
                    Enabled = true,
                    CommandType = "Vibrate",
                    FixedAmount = false,
                    Brackets = new List<CheerBracket>
                    {
                        new CheerBracket { BitAmount = 100, Intensity = 25, Duration = 1.0, Mode = SelectionMode.All },
                        new CheerBracket { BitAmount = 500, Intensity = 50, Duration = 2.0, Mode = SelectionMode.All },
                        new CheerBracket { BitAmount = 1000, Intensity = 75, Duration = 3.0, Mode = SelectionMode.All },
                    }
                }
            }
        };
    }

    private void HandleCheerEvent(Models.CheerEvent cheerEvent)
    {
        if (!_config.Enabled) return;

        var matchedSection = FindMatchingSection(cheerEvent.Message, cheerEvent.Bits);
        if (matchedSection == null) return;

        var bracket = FindMatchingBracket(matchedSection, cheerEvent.Bits);
        if (bracket == null) return;

        ExecuteAction(matchedSection, bracket);

        // Fire bracket activation trigger
        _ = _triggerManager.FireCheerBracketEvent(
            cheerEvent.UserName,
            cheerEvent.Bits,
            cheerEvent.Message,
            cheerEvent.IsAnonymous,
            matchedSection.Name,
            matchedSection.Keyword,
            bracket.BitAmount,
            bracket.Intensity,
            bracket.Duration
        );
    }

    private CheerSection? FindMatchingSection(string message, int bits)
    {
        // try to find a keyword match (non-empty keywords)
        var keywordSections = _config.Sections
            .Where(s => s.Enabled && !string.IsNullOrEmpty(s.Keyword))
            .ToList();

        foreach (var sec in keywordSections)
        {
            if (message.Contains(sec.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                // Check if this section has a valid bracket for the bit amount
                var bracket = FindMatchingBracket(sec, bits);
                if (bracket != null)
                {
                    return sec;
                }
            }
        }

        // Fall back to default section (empty keyword)
        return _config.Sections
            .FirstOrDefault(s => s.Enabled && string.IsNullOrEmpty(s.Keyword));
    }

    private CheerBracket? FindMatchingBracket(CheerSection sec, int bits)
    {
        if (!sec.Brackets.Any()) return null;

        var sortedBrackets = sec.Brackets
            .OrderByDescending(b => b.BitAmount)
            .ToList();

        if (sec.FixedAmount)
        {
            // Exact match only
            return sortedBrackets.FirstOrDefault(b => b.BitAmount == bits);
        }
        else
        {
            // Round down to nearest bracket
            return sortedBrackets.FirstOrDefault(b => b.BitAmount <= bits);
        }
    }

    private void ExecuteAction(CheerSection sec, CheerBracket bracket)
    {
        if (!sec.SelectedShockerIds.Any()) return;

        // Parse command type
        var commandType = sec.CommandType switch
        {
            "Shock" => CommandType.Shock,
            "Vibrate" => CommandType.Vibrate,
            "Beep" => CommandType.Beep,
            _ => CommandType.Vibrate
        };

        // Get shocker IDs based on selection mode
        var shockerIds = GetShockerIdsForExecution(sec, bracket.Mode);
        if (shockerIds.Count == 0) return;

        // Parse device/shocker IDs from "deviceId:shockerId" format
        var parsedIds = shockerIds
            .Select(id => id.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
            .ToList();

        if (!parsedIds.Any()) return;

        var deviceIds = parsedIds.Select(p => p.deviceId).Distinct();
        var shockerIdInts = parsedIds.Select(p => p.shockerId);

        _deviceActions.PerformAction(
            intensity: bracket.Intensity,
            durationSeconds: bracket.Duration,
            command: commandType,
            deviceIds: deviceIds,
            shockerIds: shockerIdInts
        );
    }

    private List<string> GetShockerIdsForExecution(CheerSection sec, SelectionMode mode)
    {
        var availableIds = sec.SelectedShockerIds
            .Where(id => IsShockerLoaded(id))
            .ToList();

        if (!availableIds.Any()) return [];

        return mode switch
        {
            SelectionMode.All => availableIds,
            SelectionMode.Random => new List<string> { availableIds[Random.Shared.Next(availableIds.Count)] },
            SelectionMode.RoundRobin => new List<string> { GetNextRoundRobinShocker(sec.Id, availableIds) },
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

            currentIndex %= availableIds.Count;
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

    public void AddSection(CheerSection sec)
    {
        _config.Sections.Add(sec);
        SaveConfig();
    }

    public void UpdateSection(CheerSection sec)
    {
        var index = _config.Sections.FindIndex(s => s.Id == sec.Id);
        if (index >= 0)
        {
            _config.Sections[index] = sec;
            SaveConfig();
        }
    }

    public void RemoveSection(string sectionId)
    {
        _config.Sections.RemoveAll(s => s.Id == sectionId);
        lock (_roundRobinLock)
        {
            _roundRobinIndices.Remove(sectionId);
        }
        SaveConfig();
    }

    public void AddBracket(string sectionId, CheerBracket bracket)
    {
        var sec = _config.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (sec != null)
        {
            sec.Brackets.Add(bracket);
            SaveConfig();
        }
    }

    public void UpdateBracket(string sectionId, CheerBracket bracket)
    {
        var sec = _config.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (sec != null)
        {
            var index = sec.Brackets.FindIndex(b => b.Id == bracket.Id);
            if (index >= 0)
            {
                sec.Brackets[index] = bracket;
                SaveConfig();
            }
        }
    }

    public void RemoveBracket(string sectionId, string bracketId)
    {
        var sec = _config.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (sec != null)
        {
            sec.Brackets.RemoveAll(b => b.Id == bracketId);
            SaveConfig();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        SaveConfig();
    }

    public void MoveSection(string sectionId, int direction)
    {
        var index = _config.Sections.FindIndex(s => s.Id == sectionId);
        if (index < 0) return;

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _config.Sections.Count) return;

        (_config.Sections[index], _config.Sections[newIndex]) = (_config.Sections[newIndex], _config.Sections[index]);
        SaveConfig();
    }

    public CheerSection DuplicateSection(string sectionId)
    {
        var original = _config.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (original == null) return null!;

        var duplicate = new CheerSection
        {
            Name = $"{original.Name} (Copy)",
            Keyword = original.Keyword,
            Enabled = original.Enabled,
            CommandType = original.CommandType,
            FixedAmount = original.FixedAmount,
            SelectedShockerIds = new List<string>(original.SelectedShockerIds),
            Brackets = original.Brackets.Select(b => new CheerBracket
            {
                BitAmount = b.BitAmount,
                Intensity = b.Intensity,
                Duration = b.Duration,
                Mode = b.Mode
            }).ToList()
        };

        var index = _config.Sections.FindIndex(s => s.Id == sectionId);
        _config.Sections.Insert(index + 1, duplicate);
        SaveConfig();
        return duplicate;
    }

    public void MoveBracket(string sectionId, string bracketId, int direction)
    {
        var sec = _config.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (sec == null) return;

        var index = sec.Brackets.FindIndex(b => b.Id == bracketId);
        if (index < 0) return;

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= sec.Brackets.Count) return;

        (sec.Brackets[index], sec.Brackets[newIndex]) = (sec.Brackets[newIndex], sec.Brackets[index]);
        SaveConfig();
    }

    #endregion
}
