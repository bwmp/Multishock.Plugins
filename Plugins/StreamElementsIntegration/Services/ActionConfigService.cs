using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using StreamElementsIntegration.Models;

namespace StreamElementsIntegration.Services;

/// <summary>
/// Maps incoming StreamElements events directly to shocker actions (no flow editor required).
/// Each event type holds a master enable plus one or more keyword-matched <see cref="EventSection"/>s;
/// on each event it picks the matching section (by keyword) and, for amount-bearing events, the
/// matching bracket, then runs the action.
/// </summary>
public class ActionConfigService
{
    private const string ConfigFileName = "actions-config.json";

    private readonly DeviceActionRunner _runner;
    private readonly string _configPath;
    private readonly object _saveLock = new();

    private ActionsConfig _config = new();

    public event Action? ConfigChanged;

    public ActionConfigService(IPluginHost host, DeviceActionRunner runner, StreamElementsConnectionService connection)
    {
        _runner = runner;

        var dataPath = host.GetPluginDataPath(StreamElementsIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();
        connection.OnEvent += HandleEvent;
    }

    public EventActionConfig Get(string type)
    {
        if (!_config.Events.TryGetValue(type, out var cfg))
        {
            cfg = new EventActionConfig { Type = type };
            _config.Events[type] = cfg;
        }
        return cfg;
    }

    // ----- Event handling -----

    private void HandleEvent(StreamElementsEvent e)
    {
        var cfg = Get(e.Type);
        if (!cfg.Enabled) return;

        var info = StreamElementsEventCatalog.All.FirstOrDefault(i => i.Type == e.Type);

        // Keyword matches against the user message and (for redemptions) the redeemed item name.
        var matchText = $"{e.Message} {e.Item}".Trim();
        var section = FindMatchingSection(cfg, info, matchText, e.Amount);
        if (section == null || section.SelectedShockerIds.Count == 0) return;

        int intensity;
        double duration;

        if (info is { HasAmount: true } && section.Brackets.Count > 0)
        {
            var bracket = FindBracket(section, e.Amount);
            if (bracket == null) return; // amount below the smallest bracket (or no exact match)
            intensity = bracket.Intensity;
            duration = bracket.Duration;
        }
        else
        {
            intensity = section.Intensity;
            duration = section.Duration;
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: section.Id,
            selectedShockerIds: section.SelectedShockerIds.ToList(),
            mode: section.Mode,
            commandType: section.CommandType,
            intensity: intensity,
            duration: duration,
            warningVibrate: section.WarningVibrate);
    }

    /// <summary>
    /// Picks the section that should handle an event: the first enabled section whose keyword appears in
    /// the message (and, for amount events, has a matching bracket), else the default empty-keyword section.
    /// </summary>
    private static EventSection? FindMatchingSection(EventActionConfig cfg, EventTypeInfo? info, string message, double amount)
    {
        message ??= "";
        var enabled = cfg.Sections.Where(s => s.Enabled).ToList();

        foreach (var sec in enabled.Where(s => !string.IsNullOrWhiteSpace(s.Keyword)))
        {
            if (!message.Contains(sec.Keyword, StringComparison.OrdinalIgnoreCase)) continue;
            // For amount events, only match if the section actually has a bracket for this amount.
            if (info is { HasAmount: true } && sec.Brackets.Count > 0 && FindBracket(sec, amount) == null) continue;
            return sec;
        }

        return enabled.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Keyword));
    }

    private static AmountBracket? FindBracket(EventSection sec, double amount)
    {
        var sorted = sec.Brackets.OrderByDescending(b => b.Threshold);
        return sec.FixedAmount
            ? sorted.FirstOrDefault(b => Math.Abs(b.Threshold - amount) < 0.0001)
            : sorted.FirstOrDefault(b => b.Threshold <= amount);
    }

    // ----- Persistence -----

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<ActionsConfig>(json) ?? new ActionsConfig();
                MigrateLegacy(json);
            }
        }
        catch (Exception ex)
        {
            StreamElementsIntegrationPlugin.Logger?.LogError(ex, "Failed to load actions config");
            _config = new ActionsConfig();
        }

        // Ensure every known event type has an entry so the UI can bind to it.
        foreach (var info in StreamElementsEventCatalog.All)
        {
            if (!_config.Events.ContainsKey(info.Type))
                _config.Events[info.Type] = new EventActionConfig { Type = info.Type };
        }
    }

    /// <summary>
    /// Upgrades configs written before the section model: a pre-sections event held its command/brackets
    /// directly, so wrap those legacy fields in a single "Default" section.
    /// </summary>
    private void MigrateLegacy(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in events.EnumerateObject())
            {
                var el = prop.Value;

                // Already section-based?
                if (el.TryGetProperty("Sections", out var sections)
                    && sections.ValueKind == JsonValueKind.Array
                    && sections.GetArrayLength() > 0)
                    continue;

                // Legacy events carried "CommandType"/"Brackets" at the event level.
                if (!el.TryGetProperty("CommandType", out _) && !el.TryGetProperty("Brackets", out _))
                    continue;

                var cfg = Get(prop.Name);
                if (cfg.Sections.Count > 0) continue;

                cfg.Sections.Add(new EventSection
                {
                    Name = "Default",
                    Enabled = true,
                    Keyword = "",
                    CommandType = el.TryGetProperty("CommandType", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString()! : "Vibrate",
                    WarningVibrate = el.TryGetProperty("WarningVibrate", out var wv) && wv.ValueKind == JsonValueKind.True,
                    Mode = el.TryGetProperty("Mode", out var md) ? ParseMode(md) : SelectionMode.All,
                    Intensity = el.TryGetProperty("Intensity", out var it) && it.TryGetInt32(out var iv) ? iv : 30,
                    Duration = el.TryGetProperty("Duration", out var dr) && dr.TryGetDouble(out var dv) ? dv : 1.0,
                    FixedAmount = el.TryGetProperty("FixedAmount", out var fa) && fa.ValueKind == JsonValueKind.True,
                    SelectedShockerIds = ReadStringList(el, "SelectedShockerIds"),
                    Brackets = ReadBrackets(el),
                });
            }

            Save();
        }
        catch (Exception ex)
        {
            StreamElementsIntegrationPlugin.Logger?.LogError(ex, "Failed to migrate legacy actions config");
        }
    }

    private static SelectionMode ParseMode(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && Enum.IsDefined(typeof(SelectionMode), n))
            return (SelectionMode)n;
        if (el.ValueKind == JsonValueKind.String && Enum.TryParse<SelectionMode>(el.GetString(), out var m))
            return m;
        return SelectionMode.All;
    }

    private static List<string> ReadStringList(JsonElement el, string prop)
    {
        var list = new List<string>();
        if (el.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(item.GetString()!);
        return list;
    }

    private static List<AmountBracket> ReadBrackets(JsonElement el)
    {
        var list = new List<AmountBracket>();
        if (el.TryGetProperty("Brackets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var b in arr.EnumerateArray())
                list.Add(new AmountBracket
                {
                    Id = b.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : Guid.NewGuid().ToString(),
                    Threshold = b.TryGetProperty("Threshold", out var th) && th.TryGetDouble(out var tv) ? tv : 1,
                    Intensity = b.TryGetProperty("Intensity", out var it) && it.TryGetInt32(out var iv) ? iv : 50,
                    Duration = b.TryGetProperty("Duration", out var dr) && dr.TryGetDouble(out var dv) ? dv : 1.0,
                });
        return list;
    }

    public void Save()
    {
        lock (_saveLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                StreamElementsIntegrationPlugin.Logger?.LogError(ex, "Failed to save actions config");
            }
        }
        ConfigChanged?.Invoke();
    }

    // ----- CRUD helpers for the UI -----

    public void SetEventEnabled(string type, bool enabled)
    {
        Get(type).Enabled = enabled;
        Save();
    }

    public EventSection AddSection(string type)
    {
        var cfg = Get(type);
        var section = new EventSection
        {
            Name = $"Section {cfg.Sections.Count + 1}",
            Enabled = true,
        };
        cfg.Sections.Add(section);
        Save();
        return section;
    }

    public void RemoveSection(string type, string sectionId)
    {
        Get(type).Sections.RemoveAll(s => s.Id == sectionId);
        Save();
    }

    public void MoveSection(string type, string sectionId, int direction)
    {
        var sections = Get(type).Sections;
        var index = sections.FindIndex(s => s.Id == sectionId);
        if (index < 0) return;

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= sections.Count) return;

        (sections[index], sections[newIndex]) = (sections[newIndex], sections[index]);
        Save();
    }

    public EventSection? DuplicateSection(string type, string sectionId)
    {
        var sections = Get(type).Sections;
        var original = sections.FirstOrDefault(s => s.Id == sectionId);
        if (original == null) return null;

        var duplicate = new EventSection
        {
            Name = $"{original.Name} (Copy)",
            Enabled = original.Enabled,
            Keyword = original.Keyword,
            CommandType = original.CommandType,
            WarningVibrate = original.WarningVibrate,
            Mode = original.Mode,
            Intensity = original.Intensity,
            Duration = original.Duration,
            FixedAmount = original.FixedAmount,
            SelectedShockerIds = new List<string>(original.SelectedShockerIds),
            Brackets = original.Brackets.Select(b => new AmountBracket
            {
                Threshold = b.Threshold,
                Intensity = b.Intensity,
                Duration = b.Duration,
            }).ToList(),
        };

        sections.Insert(sections.FindIndex(s => s.Id == sectionId) + 1, duplicate);
        Save();
        return duplicate;
    }

    public void AddBracket(string type, string sectionId)
    {
        var section = Get(type).Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null) return;
        var lastThreshold = section.Brackets.Count > 0 ? section.Brackets.Max(b => b.Threshold) : 0;
        section.Brackets.Add(new AmountBracket { Threshold = lastThreshold + 1, Intensity = 50, Duration = 1.0 });
        Save();
    }

    public void RemoveBracket(string type, string sectionId, string bracketId)
    {
        var section = Get(type).Sections.FirstOrDefault(s => s.Id == sectionId);
        section?.Brackets.RemoveAll(b => b.Id == bracketId);
        Save();
    }
}
