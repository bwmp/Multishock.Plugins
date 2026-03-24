using MultiShock.PluginSdk.Flow;

namespace SpeechToText.Nodes;

public sealed class WordRecognizedTriggerNode : SpeechToTextEventNodeBase
{
    public override string TypeId => "speechtotext.word";

    public override string DisplayName => "Word Recognized";

    public override string? Description => "Triggers for each individual recognized word";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("word", "Word"),
        FlowPort.String("normalizedWord", "Normalized Word"),
        FlowPort.Number("wordIndex", "Word Index"),
        FlowPort.String("sentence", "Sentence"),
        FlowPort.Number("confidence", "Confidence"),
        FlowPort.String("timestamp", "Timestamp (UTC)"),
        FlowPort.String("culture", "Culture"),
        FlowPort.String("context", "Context"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minConfidence"] = FlowProperty.Double("Min Confidence", 0.0, 0.0, 1.0, 0.01, "Only trigger for confidence above this value"),
        ["wordFilter"] = FlowProperty.String("Word Filter", "", "Word value used with the match mode"),
        ["matchMode"] = FlowProperty.Select("Match Mode",
        [
            new FlowPropertyOption { Value = "any", Label = "Any" },
            new FlowPropertyOption { Value = "contains", Label = "Contains" },
            new FlowPropertyOption { Value = "exact", Label = "Exact" },
        ], "any", "How to match the word filter"),
        ["caseSensitive"] = FlowProperty.Bool("Case Sensitive", false),
    };
}
