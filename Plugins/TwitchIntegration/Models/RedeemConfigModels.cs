namespace TwitchIntegrationPlugin.Models;

/// <summary>
/// Configuration for a single channel point redemption.
/// </summary>
public class RedeemConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The Twitch reward ID (from the API).
    /// </summary>
    public string RewardId { get; set; } = "";

    /// <summary>
    /// The reward title (for display purposes).
    /// </summary>
    public string RewardTitle { get; set; } = "";

    /// <summary>
    /// The point cost of the reward.
    /// </summary>
    public int Cost { get; set; }

    /// <summary>
    /// Whether this redemption action is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int Intensity { get; set; } = 50;

    public double Duration { get; set; } = 1.0;

    public SelectionMode Mode { get; set; } = SelectionMode.All;

    public string CommandType { get; set; } = "Shock";

    public List<string> SelectedShockerIds { get; set; } = new();
}

/// <summary>
/// Root configuration for all channel point redemptions.
/// </summary>
public class RedeemConfigRoot
{
    /// <summary>
    /// Master enable/disable for all redemption actions.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Last time the rewards were fetched from Twitch.
    /// </summary>
    public DateTime? LastFetchedUtc { get; set; }

    /// <summary>
    /// Individual redemption configurations keyed by reward ID.
    /// </summary>
    public List<RedeemConfig> Redeems { get; set; } = new();
}
