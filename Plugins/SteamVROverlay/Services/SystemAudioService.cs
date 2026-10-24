using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using NAudio.CoreAudioApi;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Services;

/// <summary>
/// Volume and mute for Windows' default playback and recording devices. The defaults are
/// re-resolved on every refresh because SteamVR and headsets switch them while running.
/// Core Audio objects created on an STA thread, such as the UI's, are tied to it, so callers
/// stay on the overlay loop or the thread pool.
/// </summary>
public sealed class SystemAudioService : IDisposable
{
    private readonly ILogger? _logger;
    private readonly object _sync = new();

    private MMDeviceEnumerator? _enumerator;
    private Endpoint? _output;
    private Endpoint? _microphone;
    private bool _failureLogged;
    private volatile AudioState? _current;

    public SystemAudioService(IServiceProvider services)
    {
        var host = services.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = host?.CreateLogger("SteamVROverlay.Audio");
    }

    /// <summary>The state as of the last refresh or change, or null when audio is unavailable.</summary>
    public AudioState? Current => _current;

    public AudioState? Refresh()
    {
        lock (_sync)
        {
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                _output = Resolve(_output, DataFlow.Render);
                _microphone = Resolve(_microphone, DataFlow.Capture);
                _current = Read();
                _failureLogged = false;
            }
            catch (Exception ex)
            {
                if (!_failureLogged)
                {
                    _logger?.LogWarning(ex, "Could not read the Windows audio devices");
                    _failureLogged = true;
                }

                ReleaseDevices();
                _current = null;
            }

            return _current;
        }
    }

    /// <summary>
    /// Sets the playback volume. Like the Windows volume flyout, moving the slider unmutes.
    /// </summary>
    public void SetOutputVolume(float level)
    {
        Change(() =>
        {
            if (_output is not { } output)
            {
                return;
            }

            var volume = output.Device.AudioEndpointVolume;
            volume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
            if (volume.Mute)
            {
                volume.Mute = false;
            }
        });
    }

    public void ToggleOutputMute() => Change(() => ToggleMute(_output));

    public void ToggleMicrophoneMute() => Change(() => ToggleMute(_microphone));

    private void Change(Action change)
    {
        lock (_sync)
        {
            try
            {
                change();
                _current = Read();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not change the Windows audio device");
                ReleaseDevices();
                _current = null;
            }
        }
    }

    private static void ToggleMute(Endpoint? endpoint)
    {
        if (endpoint is null)
        {
            return;
        }

        var volume = endpoint.Device.AudioEndpointVolume;
        volume.Mute = !volume.Mute;
    }

    private Endpoint? Resolve(Endpoint? current, DataFlow flow)
    {
        if (!_enumerator!.HasDefaultAudioEndpoint(flow, Role.Multimedia))
        {
            current?.Device.Dispose();
            return null;
        }

        var device = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        if (current is not null && current.Id == device.ID)
        {
            device.Dispose();
            return current;
        }

        current?.Device.Dispose();
        return new Endpoint(device, device.ID, device.FriendlyName);
    }

    private AudioState Read()
    {
        var outputVolume = _output?.Device.AudioEndpointVolume;
        return new AudioState(
            _output?.Name,
            outputVolume?.MasterVolumeLevelScalar ?? 0f,
            outputVolume?.Mute ?? false,
            _microphone?.Name,
            _microphone?.Device.AudioEndpointVolume.Mute ?? false);
    }

    private void ReleaseDevices()
    {
        _output?.Device.Dispose();
        _microphone?.Device.Dispose();
        _output = null;
        _microphone = null;
        _enumerator?.Dispose();
        _enumerator = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ReleaseDevices();
        }
    }

    /// <summary>A device with its identity and name cached, since reading them queries the property store.</summary>
    private sealed record Endpoint(MMDevice Device, string Id, string Name);
}
