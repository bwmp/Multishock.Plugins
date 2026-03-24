using MultiShock.PluginSdk.Flow;
using System.Text.RegularExpressions;

namespace SpeechToText.Services;

public class SpeechToTextTriggerManager
{
    private static readonly Regex WordRegex = new("[\\p{L}\\p{N}][\\p{L}\\p{N}'_-]*", RegexOptions.Compiled);

    private readonly SpeechRecognitionService _recognitionService;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public SpeechToTextTriggerManager(SpeechRecognitionService recognitionService)
    {
        _recognitionService = recognitionService;
        _recognitionService.SentenceRecognized += HandleSentenceRecognized;
    }

    public void Register(string eventType, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.ContainsKey(eventType))
            {
                _registrations[eventType] = [];
            }
            _registrations[eventType].Add(new TriggerRegistration(instance, callback));
        }
    }

    public void Unregister(string eventType, IFlowNodeInstance instance)
    {
        lock (_lock)
        {
            if (_registrations.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(r => r.Instance.InstanceId == instance.InstanceId);
            }
        }
    }

    private async Task FireEventAsync(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                return;
            }
            registrations = list.ToList();
        }

        foreach (var reg in registrations)
        {
            try
            {
                if (filter == null || filter(reg.Instance))
                {
                    await reg.Callback(reg.Instance, outputs);
                }
            }
            catch
            {
                // Ignore errors in individual triggers
            }
        }
    }

    private void HandleSentenceRecognized(SpeechSentenceEvent sentence)
    {
        var timestamp = sentence.TimestampUtc.ToString("O");
        var words = ExtractWords(sentence.Text);

        _ = FireEventAsync("speechtotext.sentence", new Dictionary<string, object?>
        {
            ["text"] = sentence.Text,
            ["confidence"] = sentence.Confidence,
            ["timestamp"] = timestamp,
            ["culture"] = sentence.Culture,
            ["wordCount"] = words.Count,
            ["context"] = sentence.Context,
        }, instance => SentenceFilterMatches(instance, sentence));

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];

            _ = FireEventAsync("speechtotext.word", new Dictionary<string, object?>
            {
                ["word"] = word,
                ["normalizedWord"] = word.ToLowerInvariant(),
                ["wordIndex"] = i,
                ["sentence"] = sentence.Text,
                ["confidence"] = sentence.Confidence,
                ["timestamp"] = timestamp,
                ["culture"] = sentence.Culture,
                ["context"] = sentence.Context,
            }, instance => WordFilterMatches(instance, sentence, word));
        }
    }

    private static bool SentenceFilterMatches(IFlowNodeInstance instance, SpeechSentenceEvent sentence)
    {
        var minConfidence = instance.GetConfig("minConfidence", 0.0);
        if (sentence.Confidence < minConfidence)
        {
            return false;
        }

        var textFilter = instance.GetConfig("textFilter", "");
        if (string.IsNullOrWhiteSpace(textFilter))
        {
            return true;
        }

        var matchMode = instance.GetConfig("matchMode", "any");
        var caseSensitive = instance.GetConfig("caseSensitive", false);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return matchMode switch
        {
            "contains" => sentence.Text.Contains(textFilter, comparison),
            "exact" => sentence.Text.Equals(textFilter, comparison),
            _ => true,
        };
    }

    private static bool WordFilterMatches(IFlowNodeInstance instance, SpeechSentenceEvent sentence, string word)
    {
        var minConfidence = instance.GetConfig("minConfidence", 0.0);
        if (sentence.Confidence < minConfidence)
        {
            return false;
        }

        var wordFilter = instance.GetConfig("wordFilter", "");
        if (string.IsNullOrWhiteSpace(wordFilter))
        {
            return true;
        }

        var matchMode = instance.GetConfig("matchMode", "any");
        var caseSensitive = instance.GetConfig("caseSensitive", false);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return matchMode switch
        {
            "contains" => word.Contains(wordFilter, comparison),
            "exact" => word.Equals(wordFilter, comparison),
            _ => true,
        };
    }

    private static List<string> ExtractWords(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return [];
        }

        var matches = WordRegex.Matches(sentence);
        if (matches.Count == 0)
        {
            return [];
        }

        return matches.Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
