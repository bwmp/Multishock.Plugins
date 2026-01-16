namespace TwitchIntegration.Models;

public class FollowConfig
{
    public bool Enabled { get; set; } = true;

    public bool FetchFollowersOnStartup { get; set; } = true;

    public int Intensity { get; set; } = 30;

    public double Duration { get; set; } = 1.0;

    public SelectionMode Mode { get; set; } = SelectionMode.All;

    public string CommandType { get; set; } = "Shock";

    public List<string> SelectedShockerIds { get; set; } = new();

    public List<string> KnownFollowerIds { get; set; } = new();

    public DateTime? LastFetchedUtc { get; set; }
}
