namespace TikTokIntegration.Models;

public enum SelectionMode
{
    All,
    Random,
    RoundRobin
}

/// <summary>A coin-value threshold bracket, mirroring the Twitch cheer bracket concept.</summary>
public class GiftBracket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public int CoinAmount { get; set; } = 1;

    public int Intensity { get; set; } = 50;

    public double Duration { get; set; } = 1.0;

    public SelectionMode Mode { get; set; } = SelectionMode.All;
}

public class GiftConfig
{
    public bool Enabled { get; set; } = true;

    public string CommandType { get; set; } = "Vibrate";

    public bool WarningVibrate { get; set; } = false;

    /// <summary>When true, brackets only match the exact coin amount instead of rounding down.</summary>
    public bool FixedAmount { get; set; } = false;

    /// <summary>
    /// When true, streakable gifts trigger on every combo tick instead of once when the
    /// streak completes. Off by default so a 50x combo counts as one gift worth 50x coins.
    /// </summary>
    public bool TriggerDuringStreaks { get; set; } = false;

    public List<string> SelectedShockerIds { get; set; } = new();

    public List<GiftBracket> Brackets { get; set; } = new();
}

/// <summary>A per-gift override rule with its own action settings, taking priority over brackets.</summary>
public class GiftRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public bool Enabled { get; set; } = true;

    /// <summary>TikTok gift id when known (picked from the catalog); 0 = match by name only.</summary>
    public int GiftId { get; set; }

    public string GiftName { get; set; } = "";

    public string CommandType { get; set; } = "Vibrate";

    public int Intensity { get; set; } = 50;

    public double Duration { get; set; } = 1.0;

    public SelectionMode Mode { get; set; } = SelectionMode.All;

    public bool WarningVibrate { get; set; } = false;

    /// <summary>Multiply duration by the streak repeat count (capped by MaxDuration).</summary>
    public bool ScaleWithRepeatCount { get; set; } = false;

    public double MaxDuration { get; set; } = 15.0;

    public List<string> SelectedShockerIds { get; set; } = new();
}

public class GiftRulesConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>When a rule matches, skip the coin bracket processing for that gift.</summary>
    public bool RulesOverrideBrackets { get; set; } = true;

    public List<GiftRule> Rules { get; set; } = new();
}

/// <summary>Simple one-shot action settings shared by follow/share/subscribe/join handlers.</summary>
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

public class LikeActionConfig : SimpleActionConfig
{
    /// <summary>Trigger once every N total likes (likes arrive in batches).</summary>
    public int EveryNLikes { get; set; } = 100;
}

/// <summary>
/// A cumulative "goal meter": gift coin values add up over time and fire an action each time the
/// running total crosses <see cref="GoalAmount"/>. Builds toward a goal instead of reacting to a
/// single gift like the brackets do.
/// </summary>
public class CoinGoalConfig : SimpleActionConfig
{
    public int GoalAmount { get; set; } = 100;

    /// <summary>Reset the accumulated progress back to zero when the stream ends.</summary>
    public bool ResetOnStreamEnd { get; set; } = true;
}

public class EventsConfig
{
    public SimpleActionConfig Follow { get; set; } = new();

    public SimpleActionConfig Share { get; set; } = new();

    public SimpleActionConfig Subscribe { get; set; } = new();

    public SimpleActionConfig Join { get; set; } = new();

    public LikeActionConfig Likes { get; set; } = new();
}
