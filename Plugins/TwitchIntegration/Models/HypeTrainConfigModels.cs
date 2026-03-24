namespace TwitchIntegration.Models;

public enum HypeTrainScalingMode
{
    Fixed,
    Incremental
}

public class HypeTrainActionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public bool Enabled { get; set; }

    public List<string> SelectedShockerIds { get; set; } = new();

    public SelectionMode Mode { get; set; } = SelectionMode.All;

    public string CommandType { get; set; } = "Shock";

    public HypeTrainScalingMode ScalingMode { get; set; } = HypeTrainScalingMode.Fixed;

    public int Intensity { get; set; } = 50;

    public double Duration { get; set; } = 1.0;

    public int IncrementIntensity { get; set; }

    public double IncrementDuration { get; set; }

    public int MaxIntensity { get; set; } = 100;

    public double MaxDuration { get; set; } = 15.0;
}

public class HypeTrainConfig
{
    public bool Enabled { get; set; } = true;

    public HypeTrainActionConfig During { get; set; } = new();

    public HypeTrainActionConfig End { get; set; } = new();
}
