namespace JoystickIntegration.Models;

// SelectionMode lives in JoystickEventModels.cs.

/// <summary>A token-value threshold bracket for tips.</summary>
public class TipBracket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int TokenAmount { get; set; } = 1;
    public int Intensity { get; set; } = 50;
    public double Duration { get; set; } = 1.0;
    public SelectionMode Mode { get; set; } = SelectionMode.All;
}

/// <summary>Token-value bracket configuration for tips (applies to any tip not matched by a tip-menu rule).</summary>
public class TipBracketsConfig
{
    public bool Enabled { get; set; } = true;
    public string CommandType { get; set; } = "Vibrate";
    public bool WarningVibrate { get; set; } = false;

    /// <summary>When true, brackets only match the exact token amount instead of rounding down.</summary>
    public bool FixedAmount { get; set; } = false;

    public List<string> SelectedShockerIds { get; set; } = new();
    public List<TipBracket> Brackets { get; set; } = new();
}

/// <summary>A per-tip-menu-item override rule with its own action settings, taking priority over brackets.</summary>
public class TipRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Enabled { get; set; } = true;

    /// <summary>The tip-menu item name to match (case-insensitive, contains).</summary>
    public string ItemName { get; set; } = "";

    public string CommandType { get; set; } = "Vibrate";
    public int Intensity { get; set; } = 50;
    public double Duration { get; set; } = 1.0;
    public SelectionMode Mode { get; set; } = SelectionMode.All;
    public bool WarningVibrate { get; set; } = false;

    /// <summary>Multiply duration proportionally to the tip's token amount (capped by MaxDuration).</summary>
    public bool ScaleWithTokens { get; set; } = false;

    /// <summary>Token amount that equals one unit of the base duration when scaling.</summary>
    public int ScaleTokenUnit { get; set; } = 1;

    public double MaxDuration { get; set; } = 15.0;

    public List<string> SelectedShockerIds { get; set; } = new();
}

public class TipRulesConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>When a rule matches, skip the token bracket processing for that tip.</summary>
    public bool RulesOverrideBrackets { get; set; } = true;

    public List<TipRule> Rules { get; set; } = new();
}

/// <summary>Simple one-shot action settings shared by follow/subscribe/wheel-spin/tip-goal handlers.</summary>
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
    public SimpleActionConfig WheelSpin { get; set; } = new();
    public SimpleActionConfig TipGoal { get; set; } = new();
}

/// <summary>
/// Optional chat feedback: posts a public chat message when events fire (requires the bot's
/// SendMessage permission). Templates support placeholders {user}, {tokens}, {item}, {prize}.
/// </summary>
public class ChatFeedbackConfig
{
    public bool Enabled { get; set; } = false;

    public bool TipEnabled { get; set; } = true;
    public string TipTemplate { get; set; } = "⚡ Thanks {user} for {tokens} tokens!";

    public bool FollowEnabled { get; set; } = false;
    public string FollowTemplate { get; set; } = "Thanks for the follow, {user}!";

    public bool SubscribeEnabled { get; set; } = false;
    public string SubscribeTemplate { get; set; } = "Welcome to the crew, {user}! 🎉";
}
