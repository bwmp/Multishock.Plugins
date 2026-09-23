using MultiShock.PluginSdk;

namespace JoystickIntegration.Services;

/// <summary>
/// Shared shocker action execution used by all Joystick event handlers:
/// selection-mode handling (all/random/round-robin), optional warning vibrate,
/// and parsing of the "deviceId:shockerId" combined identifiers.
/// Mirrors the TikTok plugin's runner.
/// </summary>
public class DeviceActionRunner
{
    private readonly IDeviceActions _deviceActions;
    private readonly Dictionary<string, int> _roundRobinIndices = [];
    private readonly object _roundRobinLock = new();

    public DeviceActionRunner(IDeviceActions deviceActions)
    {
        _deviceActions = deviceActions;
    }

    public async Task ExecuteAsync(
        string roundRobinKey,
        List<string> selectedShockerIds,
        SelectionMode mode,
        string commandType,
        int intensity,
        double duration,
        bool warningVibrate)
    {
        if (selectedShockerIds.Count == 0)
        {
            return;
        }

        var command = commandType switch
        {
            "Shock" => CommandType.Shock,
            "Vibrate" => CommandType.Vibrate,
            "Beep" => CommandType.Beep,
            _ => CommandType.Vibrate
        };

        var shockerIds = SelectShockers(roundRobinKey, selectedShockerIds, mode);
        if (shockerIds.Count == 0)
        {
            return;
        }

        var parsedIds = shockerIds
            .Select(id => id.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
            .ToList();

        if (parsedIds.Count == 0)
        {
            return;
        }

        var deviceIds = parsedIds.Select(p => p.deviceId).Distinct();
        var shockerIdInts = parsedIds.Select(p => p.shockerId);

        if (warningVibrate && command == CommandType.Shock)
        {
            _deviceActions.PerformAction(
                intensity: intensity,
                durationSeconds: 1.0,
                command: CommandType.Vibrate,
                deviceIds: deviceIds,
                shockerIds: shockerIdInts);

            await Task.Delay(1000);
        }

        _deviceActions.PerformAction(
            intensity: intensity,
            durationSeconds: duration,
            command: command,
            deviceIds: deviceIds,
            shockerIds: shockerIdInts);
    }

    private List<string> SelectShockers(string roundRobinKey, List<string> selectedIds, SelectionMode mode)
    {
        var availableIds = selectedIds.Where(IsShockerLoaded).ToList();
        if (availableIds.Count == 0)
        {
            return [];
        }

        return mode switch
        {
            SelectionMode.All => availableIds,
            SelectionMode.Random => [availableIds[Random.Shared.Next(availableIds.Count)]],
            SelectionMode.RoundRobin => [GetNextRoundRobinShocker(roundRobinKey, availableIds)],
            _ => availableIds
        };
    }

    private string GetNextRoundRobinShocker(string key, List<string> availableIds)
    {
        lock (_roundRobinLock)
        {
            if (!_roundRobinIndices.TryGetValue(key, out var currentIndex))
            {
                currentIndex = 0;
            }

            currentIndex %= availableIds.Count;
            var result = availableIds[currentIndex];
            _roundRobinIndices[key] = (currentIndex + 1) % availableIds.Count;
            return result;
        }
    }

    private bool IsShockerLoaded(string combinedId)
    {
        var parts = combinedId.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var deviceId)) return false;
        if (!int.TryParse(parts[1], out var shockerId)) return false;
        return _deviceActions.IsShockerLoaded(deviceId, shockerId);
    }
}
