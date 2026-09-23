namespace FanslyIntegration.Models;

// SelectionMode lives in FanslyEventModels.cs.

/// <summary>A tip-amount threshold bracket mapping a token value to intensity and duration.</summary>
public class TipBracket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Amount { get; set; } = 1;
    public int Intensity { get; set; } = 50;
    public double Duration { get; set; } = 1.0;
    public SelectionMode Mode { get; set; } = SelectionMode.All;
}

/// <summary>Tip-amount bracket configuration. Wires tips straight to shocker actions without the flow editor.</summary>
public class TipBracketsConfig
{
    public bool Enabled { get; set; } = true;
    public string CommandType { get; set; } = "Vibrate";
    public bool WarningVibrate { get; set; } = false;

    /// <summary>When true, brackets match the exact tip amount instead of rounding down to the highest bracket at/below it.</summary>
    public bool FixedAmount { get; set; } = false;

    public List<string> SelectedShockerIds { get; set; } = new();
    public List<TipBracket> Brackets { get; set; } = new();
}

/// <summary>Simple one-shot action settings shared by the follow/subscribe handlers.</summary>
public class SimpleActionConfig
{
    public bool Enabled { get; set; } = false;
    public string CommandType { get; set; } = "Vibrate";
    public int Intensity { get; set; } = 30;
    public double Duration { get; set; } = 1.0;
    public SelectionMode Mode { get; set; } = SelectionMode.All;
    public bool WarningVibrate { get; set; } = false;
    public List<string> SelectedShockerIds { get; set; } = new();
}

public class EventsConfig
{
    public SimpleActionConfig Follow { get; set; } = new();
    public SimpleActionConfig Subscribe { get; set; } = new();
}
