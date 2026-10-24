using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk;
using SteamVROverlay.Overlay;
using SteamVROverlay.Services;

namespace SteamVROverlay.Tests;

public sealed class OverlaySettingsTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "steamvroverlay-settings-" + Guid.NewGuid().ToString("N"));

    private OverlaySettingsService CreateSettings()
    {
        var host = DispatchProxy.Create<IPluginHost, HostProxy>();
        ((HostProxy)host).DataPath = _dataPath;
        return new OverlaySettingsService(host);
    }

    [Fact]
    public void APlacementSavedFromVrIsKeptAcrossRestarts()
    {
        var settings = CreateSettings();
        var changes = 0;
        settings.Changed += () => changes++;

        settings.SetPlacement(new OverlayPlacement(-0.1f, 0.03f, 0.15f, -5f, -80f, 170f));

        Assert.Equal(1, changes);
        Assert.Equal(new OverlayPlacement(-0.1f, 0.03f, 0.15f, -5f, -80f, 170f), CreateSettings().Placement);
    }

    [Fact]
    public void APlacementIsClampedToWhatTheSettingsCanStore()
    {
        var settings = CreateSettings();

        settings.SetPlacement(new OverlayPlacement(0.9f, -0.7f, 0.2f, 0f, 200f, -190f));

        Assert.Equal(new OverlayPlacement(0.5f, -0.5f, 0.2f, 0f, 180f, -180f), settings.Placement);
    }

    [Fact]
    public void ResettingThePlacementRestoresTheDefaults()
    {
        var settings = CreateSettings();
        var defaults = settings.Placement;
        settings.SetPlacement(new OverlayPlacement(0.2f, 0.2f, 0.2f, 45f, 45f, 45f));

        settings.ResetPlacement();

        Assert.Equal(defaults, settings.Placement);
    }

    [Fact]
    public void GrabToMoveIsOnUntilTurnedOffAndStaysOff()
    {
        var settings = CreateSettings();
        Assert.True(settings.GrabToMove);

        settings.GrabToMove = false;
        Assert.False(CreateSettings().GrabToMove);

        settings.ApplySettings(new Dictionary<string, object?> { ["grabToMove"] = true });
        Assert.True(CreateSettings().GrabToMove);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    public class HostProxy : DispatchProxy
    {
        public string DataPath { get; set; } = "";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "CreateLogger" => NullLogger.Instance,
            "GetPluginDataPath" => DataPath,
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }
}
