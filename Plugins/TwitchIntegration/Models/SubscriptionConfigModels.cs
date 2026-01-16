namespace TwitchIntegration.Models;

public class SubscriptionConfig
{
    public bool Enabled { get; set; } = true;

    public List<SubscriptionTierSection> Sections { get; set; } = new();
}

public enum SubscriptionMode
{
    Brackets,

    Incremental
}

public class SubscriptionTierSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public bool Enabled { get; set; } = true;

    public int Tier { get; set; } = 1;

    public string Name { get; set; } = "Tier 1";

    public List<string> SelectedShockerIds { get; set; } = new();

    public SubscriptionMode Mode { get; set; } = SubscriptionMode.Brackets;

    public string BracketCommandType { get; set; } = "Vibrate";

    public bool FixedAmount { get; set; } = false;

    public List<SubscriptionBracket> Brackets { get; set; } = new();

    public SubscriptionIncrementalAction Incremental { get; set; } = new();
}

public class SubscriptionBracket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public int Count { get; set; } = 1;

    public int Intensity { get; set; } = 50;

    public double Duration { get; set; } = 1.0;

    public SelectionMode Mode { get; set; } = SelectionMode.All;
}

public class SubscriptionIncrementalAction
{
    public int BaseIntensity { get; set; } = 50;

    public double BaseDuration { get; set; } = 1.0;

    public int MaxIntensity { get; set; } = 100;

    public double MaxDuration { get; set; } = 5.0;

    public int IncrementIntensity { get; set; } = 5;

    public double IncrementDuration { get; set; } = 0.5;

    public string CommandType { get; set; } = "Vibrate";

    public SelectionMode Mode { get; set; } = SelectionMode.All;
}
