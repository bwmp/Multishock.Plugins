using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Valve.VR;

namespace SteamVROverlay.Overlay;

public readonly record struct PointerState(Matrix4x4 Pose, bool TriggerDown, bool GripDown)
{
    public Vector3 Origin => OverlayMath.PositionOf(Pose);

    public Vector3 Direction => OverlayMath.AimOf(Pose);
}

/// <summary>
/// Reads the pointing hand through SteamVR Input. Unlike an interactive overlay, an action set
/// does not take input focus from the running game, so the game keeps receiving input and
/// SteamVR does not switch into its dashboard-style device display.
/// </summary>
public sealed class WristInput
{
    private const string ActionSet = "/actions/wrist";
    private const string ClickAction = "/actions/wrist/in/click";
    private const string GrabAction = "/actions/wrist/in/grab";
    private const string PointerAction = "/actions/wrist/in/pointer";
    private const string HapticAction = "/actions/wrist/out/haptic";

    // Controllers that expose trigger/click, pose/tip and output/haptic under the same paths.
    private static readonly string[] ControllerTypes =
        ["oculus_touch", "knuckles", "vive_controller", "holographic_controller", "hpmotioncontroller", "vive_cosmos_controller"];

    private static readonly uint ActiveSetSize = (uint)Marshal.SizeOf<VRActiveActionSet_t>();
    private static readonly uint DigitalSize = (uint)Marshal.SizeOf<InputDigitalActionData_t>();
    private static readonly uint PoseSize = (uint)Marshal.SizeOf<InputPoseActionData_t>();

    private readonly CVRInput _input;
    private readonly VRActiveActionSet_t[] _activeSets;
    private readonly ulong _click;
    private readonly ulong _grab;
    private readonly ulong _pointer;
    private readonly ulong _haptic;
    private readonly ulong _leftHand;
    private readonly ulong _rightHand;

    private WristInput(CVRInput input, ulong actionSet, ulong click, ulong grab, ulong pointer, ulong haptic, ulong leftHand, ulong rightHand)
    {
        _input = input;
        _activeSets = [new VRActiveActionSet_t { ulActionSet = actionSet, ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle }];
        _click = click;
        _grab = grab;
        _pointer = pointer;
        _haptic = haptic;
        _leftHand = leftHand;
        _rightHand = rightHand;
    }

    /// <summary>
    /// Writes the action manifest into <paramref name="directory"/> and registers it.
    /// </summary>
    public static WristInput Create(CVRInput input, string directory)
    {
        var manifestPath = WriteManifest(directory);
        Check(input.SetActionManifestPath(manifestPath), "SetActionManifestPath");

        ulong actionSet = 0, click = 0, grab = 0, pointer = 0, haptic = 0, left = 0, right = 0;
        Check(input.GetActionSetHandle(ActionSet, ref actionSet), "GetActionSetHandle");
        Check(input.GetActionHandle(ClickAction, ref click), "GetActionHandle(click)");
        Check(input.GetActionHandle(GrabAction, ref grab), "GetActionHandle(grab)");
        Check(input.GetActionHandle(PointerAction, ref pointer), "GetActionHandle(pointer)");
        Check(input.GetActionHandle(HapticAction, ref haptic), "GetActionHandle(haptic)");
        Check(input.GetInputSourceHandle("/user/hand/left", ref left), "GetInputSourceHandle(left)");
        Check(input.GetInputSourceHandle("/user/hand/right", ref right), "GetInputSourceHandle(right)");

        return new WristInput(input, actionSet, click, grab, pointer, haptic, left, right);
    }

    /// <summary>
    /// Refreshes action state. Call once per frame before reading.
    /// </summary>
    public void Update() => _input.UpdateActionState(_activeSets, ActiveSetSize);

    public bool TryReadPointer(bool rightHand, out PointerState state)
    {
        state = default;
        var hand = rightHand ? _rightHand : _leftHand;

        var pose = new InputPoseActionData_t();
        if (_input.GetPoseActionDataRelativeToNow(
                _pointer,
                ETrackingUniverseOrigin.TrackingUniverseStanding,
                0f,
                ref pose,
                PoseSize,
                hand) != EVRInputError.None ||
            !pose.bActive ||
            !pose.pose.bPoseIsValid)
        {
            return false;
        }

        state = new PointerState(
            OverlayMath.ToMatrix4x4(pose.pose.mDeviceToAbsoluteTracking),
            IsDown(_click, hand),
            IsDown(_grab, hand));
        return true;
    }

    private bool IsDown(ulong action, ulong hand)
    {
        var digital = new InputDigitalActionData_t();
        return _input.GetDigitalActionData(action, ref digital, DigitalSize, hand) == EVRInputError.None &&
               digital.bActive &&
               digital.bState;
    }

    public void Pulse(bool rightHand, float amplitude = 0.25f, float seconds = 0.015f) =>
        _input.TriggerHapticVibrationAction(_haptic, 0f, seconds, 160f, amplitude, rightHand ? _rightHand : _leftHand);

    private static string WriteManifest(string directory)
    {
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };

        foreach (var controllerType in ControllerTypes)
        {
            File.WriteAllText(
                Path.Combine(directory, $"bindings_{controllerType}.json"),
                JsonSerializer.Serialize(BuildBindings(controllerType), options));
        }

        var manifest = new Dictionary<string, object>
        {
            ["default_bindings"] = ControllerTypes
                .Select(type => new Dictionary<string, string>
                {
                    ["controller_type"] = type,
                    ["binding_url"] = $"bindings_{type}.json"
                })
                .ToArray(),
            ["actions"] = new[]
            {
                new Dictionary<string, string> { ["name"] = ClickAction, ["type"] = "boolean" },
                new Dictionary<string, string> { ["name"] = GrabAction, ["type"] = "boolean" },
                new Dictionary<string, string> { ["name"] = PointerAction, ["type"] = "pose" },
                new Dictionary<string, string> { ["name"] = HapticAction, ["type"] = "vibration" },
            },
            ["action_sets"] = new[]
            {
                new Dictionary<string, string> { ["name"] = ActionSet, ["usage"] = "leftright" },
            },
            ["localization"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["language_tag"] = "en_US",
                    [ActionSet] = "MultiShock Wrist",
                    [ClickAction] = "Click",
                    [GrabAction] = "Move the panel",
                    [PointerAction] = "Pointer",
                    [HapticAction] = "Haptics",
                },
            },
        };

        var manifestPath = Path.Combine(directory, "actions.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, options));
        return manifestPath;
    }

    private static Dictionary<string, object> BuildBindings(string controllerType)
    {
        string[] hands = ["left", "right"];
        return new Dictionary<string, object>
        {
            ["controller_type"] = controllerType,
            ["bindings"] = new Dictionary<string, object>
            {
                [ActionSet] = new Dictionary<string, object>
                {
                    ["sources"] = hands.SelectMany(hand => new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["path"] = $"/user/hand/{hand}/input/trigger",
                            ["mode"] = "trigger",
                            ["inputs"] = new Dictionary<string, object>
                            {
                                ["click"] = new Dictionary<string, string> { ["output"] = ClickAction },
                            },
                        },
                        GripSource(controllerType, hand),
                    }).ToArray(),
                    ["poses"] = hands.Select(hand => new Dictionary<string, string>
                    {
                        ["path"] = $"/user/hand/{hand}/pose/tip",
                        ["output"] = PointerAction,
                    }).ToArray(),
                    ["haptics"] = hands.Select(hand => new Dictionary<string, string>
                    {
                        ["path"] = $"/user/hand/{hand}/output/haptic",
                        ["output"] = HapticAction,
                    }).ToArray(),
                },
            },
        };
    }

    /// <summary>
    /// Index controllers sense grip force rather than clicking, so they use SteamVR's grab mode;
    /// every other supported controller's grip reads as a button.
    /// </summary>
    private static Dictionary<string, object> GripSource(string controllerType, string hand)
    {
        var (mode, input) = controllerType == "knuckles" ? ("grab", "grab") : ("button", "click");
        return new Dictionary<string, object>
        {
            ["path"] = $"/user/hand/{hand}/input/grip",
            ["mode"] = mode,
            ["inputs"] = new Dictionary<string, object>
            {
                [input] = new Dictionary<string, string> { ["output"] = GrabAction },
            },
        };
    }

    private static void Check(EVRInputError error, string call)
    {
        if (error != EVRInputError.None)
        {
            throw new InvalidOperationException($"{call} failed: {error}");
        }
    }
}
