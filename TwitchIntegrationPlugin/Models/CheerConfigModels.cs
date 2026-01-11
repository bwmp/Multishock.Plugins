namespace TwitchIntegrationPlugin.Models;

public enum SelectionMode
{
    All,
    Random,
    RoundRobin
}

public class CheerBracket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public int BitAmount { get; set; } = 100;

    public int Intensity { get; set; } = 50;

    public double Duration { get; set; } = 1.0;

    public SelectionMode Mode { get; set; } = SelectionMode.All;
}

public class CheerSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public bool Enabled { get; set; } = true;

    public string Keyword { get; set; } = "";

    public string Name { get; set; } = "Default";

    public List<string> SelectedShockerIds { get; set; } = new();

    public string CommandType { get; set; } = "Vibrate";

    public bool FixedAmount { get; set; } = false;

    public List<CheerBracket> Brackets { get; set; } = new();
}

public class CheerConfig
{
    public bool Enabled { get; set; } = true;

    public List<CheerSection> Sections { get; set; } = new();
}
