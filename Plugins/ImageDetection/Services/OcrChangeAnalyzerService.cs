using System.Globalization;
using System.Text.RegularExpressions;
using ImageDetection.Models;
using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Stateful analyzer for OCR readings. Handles keyword matching, number parsing,
/// smoothing, and cooldown/debouncing.
/// </summary>
public sealed class OcrChangeAnalyzerService(ILogger? logger = null)
{
    private readonly ILogger? _logger = logger;
    private readonly Dictionary<string, OcrState> _states = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Builds a normalized OCR reading from raw OCR text.
    /// </summary>
    public OcrReading ParseReading(OcrDetectionConfig config, string? rawText)
    {
        var reading = new OcrReading
        {
            RawText = rawText?.Trim() ?? string.Empty,
            NormalizedText = NormalizeText(rawText)
        };

        if ((config.Mode == OcrDetectionMode.Keyword || config.Mode == OcrDetectionMode.KeywordOrNumber)
            && config.Keywords.Count > 0
            && !string.IsNullOrWhiteSpace(reading.NormalizedText))
        {
            var comparison = config.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            reading.MatchedKeyword = config.Keywords
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .FirstOrDefault(keyword => reading.NormalizedText.Contains(keyword, comparison));
        }

        if (config.Mode == OcrDetectionMode.Number || config.Mode == OcrDetectionMode.KeywordOrNumber)
        {
            reading.NumberValue = TryExtractNumber(reading.NormalizedText, config.NumberRegex);
        }

        return reading;
    }

    /// <summary>
    /// Processes an OCR reading and emits a single event when thresholds are met.
    /// </summary>
    public OcrDetectionEvent? Process(string moduleId, DetectionImage target, OcrReading reading, DateTime timestampUtc)
    {
        var key = $"{moduleId}/{target.Id}";
        var config = target.Ocr;

        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new OcrState();
                _states[key] = state;
            }

            state.LastRawText = reading.RawText;
            state.LastNormalizedText = reading.NormalizedText;
            state.LastMatchedKeyword = reading.MatchedKeyword;
            state.LastParsedNumber = reading.NumberValue;
            state.LastSeenTime = timestampUtc;

            var numberEvent = TryBuildNumberEvent(moduleId, target, reading, timestampUtc, state, config);
            if (numberEvent != null)
            {
                _logger?.LogDebug("OCR number event {Key}: {Previous} -> {Current} ({Delta})",
                    key,
                    numberEvent.PreviousNumber,
                    numberEvent.CurrentNumber,
                    numberEvent.DeltaNumber);
                return numberEvent;
            }

            if (string.IsNullOrWhiteSpace(reading.MatchedKeyword))
            {
                state.LastFrameHadKeyword = false;
            }

            var keywordEvent = TryBuildKeywordEvent(moduleId, target, reading, timestampUtc, state, config);
            if (keywordEvent != null)
            {
                _logger?.LogDebug("OCR keyword event {Key}: {Keyword}", key, keywordEvent.MatchedKeyword);
                return keywordEvent;
            }

            return null;
        }
    }

    /// <summary>
    /// Returns the current smoothed number value for a target, or null if unavailable.
    /// </summary>
    public double? GetCurrentValue(string moduleId, string targetId)
    {
        var key = $"{moduleId}/{targetId}";
        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var state) || !state.HasNumberBaseline)
            {
                return null;
            }

            return state.RecentNumbers.Count > 0
                ? state.RecentNumbers.Average()
                : state.LastStableNumber;
        }
    }

    /// <summary>
    /// Returns the most recent OCR reading for a target, or null if unavailable.
    /// </summary>
    public (string RawText, string NormalizedText, string? MatchedKeyword, double? NumberValue)? GetLastReading(
        string moduleId,
        string targetId)
    {
        var key = $"{moduleId}/{targetId}";
        lock (_lock)
        {
            if (!_states.TryGetValue(key, out var state) || state.LastSeenTime == DateTime.MinValue)
            {
                return null;
            }

            return (
                state.LastRawText,
                state.LastNormalizedText,
                state.LastMatchedKeyword,
                state.LastParsedNumber);
        }
    }

    /// <summary>
    /// Resets state for one target.
    /// </summary>
    public void ResetTarget(string moduleId, string targetId)
    {
        var key = $"{moduleId}/{targetId}";
        lock (_lock)
        {
            _ = _states.Remove(key);
        }
    }

    /// <summary>
    /// Resets all OCR analyzer state.
    /// </summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            _states.Clear();
        }
    }

    private static OcrDetectionEvent? TryBuildNumberEvent(
        string moduleId,
        DetectionImage target,
        OcrReading reading,
        DateTime timestampUtc,
        OcrState state,
        OcrDetectionConfig config)
    {
        if ((config.Mode != OcrDetectionMode.Number && config.Mode != OcrDetectionMode.KeywordOrNumber)
            || reading.NumberValue == null)
        {
            return null;
        }

        var value = reading.NumberValue.Value;

        state.RecentNumbers.Enqueue(value);
        var maxFrames = Math.Clamp(config.SmoothingFrames, 1, 10);
        while (state.RecentNumbers.Count > maxFrames)
        {
            _ = state.RecentNumbers.Dequeue();
        }

        var smoothed = state.RecentNumbers.Average();

        if (!state.HasNumberBaseline)
        {
            state.HasNumberBaseline = true;
            state.LastStableNumber = smoothed;
            return null;
        }

        var delta = smoothed - state.LastStableNumber;
        var absDelta = Math.Abs(delta);
        if (absDelta < Math.Max(0.01, config.MinNumberDelta))
        {
            return null;
        }

        if ((timestampUtc - state.LastNumberEventTime).TotalMilliseconds < Math.Max(50, config.EventCooldownMs))
        {
            return null;
        }

        var changeType = delta > 0
            ? OcrChangeType.NumberIncreased
            : OcrChangeType.NumberDecreased;

        var evt = new OcrDetectionEvent
        {
            ModuleId = moduleId,
            TargetId = target.Id,
            TargetName = target.Name,
            RawText = reading.RawText,
            NormalizedText = reading.NormalizedText,
            MatchedKeyword = reading.MatchedKeyword,
            CurrentNumber = Math.Round(smoothed, 2),
            PreviousNumber = Math.Round(state.LastStableNumber, 2),
            DeltaNumber = Math.Round(delta, 2),
            ChangeType = changeType,
            Timestamp = timestampUtc
        };

        state.LastStableNumber = smoothed;
        state.LastNumberEventTime = timestampUtc;

        return evt;
    }

    private static OcrDetectionEvent? TryBuildKeywordEvent(
        string moduleId,
        DetectionImage target,
        OcrReading reading,
        DateTime timestampUtc,
        OcrState state,
        OcrDetectionConfig config)
    {
        if ((config.Mode != OcrDetectionMode.Keyword && config.Mode != OcrDetectionMode.KeywordOrNumber)
            || string.IsNullOrWhiteSpace(reading.MatchedKeyword))
        {
            return null;
        }

        if ((timestampUtc - state.LastKeywordEventTime).TotalMilliseconds < Math.Max(50, config.EventCooldownMs))
        {
            return null;
        }

        if (state.LastFrameHadKeyword)
        {
            return null;
        }

        state.LastKeywordEventTime = timestampUtc;
        state.LastFrameHadKeyword = true;

        return new OcrDetectionEvent
        {
            ModuleId = moduleId,
            TargetId = target.Id,
            TargetName = target.Name,
            RawText = reading.RawText,
            NormalizedText = reading.NormalizedText,
            MatchedKeyword = reading.MatchedKeyword,
            CurrentNumber = reading.NumberValue,
            ChangeType = OcrChangeType.KeywordMatched,
            Timestamp = timestampUtc
        };
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var collapsed = Regex.Replace(text, @"\s+", " ");
        return collapsed.Trim();
    }

    private static double? TryExtractNumber(string normalizedText, string pattern)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return null;
        }

        var regexPattern = string.IsNullOrWhiteSpace(pattern) ? @"-?\d+(?:\.\d+)?" : pattern;
        Match match;
        try
        {
            match = Regex.Match(normalizedText, regexPattern);
        }
        catch
        {
            match = Regex.Match(normalizedText, @"-?\d+(?:\.\d+)?");
        }

        if (!match.Success)
        {
            return null;
        }

        var valueText = match.Value.Replace(',', '.');
        return double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private sealed class OcrState
    {
        public Queue<double> RecentNumbers { get; } = new();
        public bool HasNumberBaseline { get; set; }
        public double LastStableNumber { get; set; }
        public DateTime LastNumberEventTime { get; set; } = DateTime.MinValue;
        public DateTime LastKeywordEventTime { get; set; } = DateTime.MinValue;
        public bool LastFrameHadKeyword { get; set; }
        public string LastRawText { get; set; } = string.Empty;
        public string LastNormalizedText { get; set; } = string.Empty;
        public string? LastMatchedKeyword { get; set; }
        public double? LastParsedNumber { get; set; }
        public DateTime LastSeenTime { get; set; } = DateTime.MinValue;
    }
}
