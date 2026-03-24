using MultiShock.PluginSdk.Flow;

namespace SpeechToText.Nodes;

public sealed class SentenceRecognizedTriggerNode : SpeechToTextEventNodeBase
{
    public override string TypeId => "speechtotext.sentence";

    public override string DisplayName => "Sentence Recognized";

    public override string? Description => "Triggers when a spoken sentence is recognized";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("text", "Text"),
        FlowPort.Number("confidence", "Confidence"),
        FlowPort.String("timestamp", "Timestamp (UTC)"),
        FlowPort.String("culture", "Culture"),
        FlowPort.Number("wordCount", "Word Count"),
        FlowPort.String("context", "Context"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minConfidence"] = FlowProperty.Double("Min Confidence", 0.0, 0.0, 1.0, 0.01, "Only trigger for confidence above this value"),
        ["textFilter"] = FlowProperty.String("Text Filter", "", "Text value used with the match mode"),
        ["matchMode"] = FlowProperty.Select("Match Mode",
        [
            new FlowPropertyOption { Value = "any", Label = "Any" },
            new FlowPropertyOption { Value = "contains", Label = "Contains" },
            new FlowPropertyOption { Value = "exact", Label = "Exact" },
        ], "any", "How to match the text filter"),
        ["caseSensitive"] = FlowProperty.Bool("Case Sensitive", false),
    };
}
