using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk.Flow;
using RandomNodes;
using RandomNodes.Nodes;
using RandomNodes.Services;

namespace PortablePlugins.Tests;

public sealed class MediaNodeTests
{
    [Fact]
    public void RegistersMediaBackendForTheHostPlatformWithoutStartingIt()
    {
        var services = new ServiceCollection();
        new RandomNodesPlugin().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISystemMediaSessionService>();
        var expected = OperatingSystem.IsWindows()
            ? "WindowsMediaSessionService"
            : nameof(ProcessMediaSessionService);
        Assert.Equal(expected, service.GetType().Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaNodesKeepTheirPortsAndSpotifyPreference(bool spotify)
    {
        var media = new FakeMediaService();
        using var provider = new ServiceCollection().AddSingleton<ISystemMediaSessionService>(media).BuildServiceProvider();
        IFlowProcessNode node = spotify ? new GetSpotifyNowPlayingNode() : new GetActiveMediaSessionNode();
        var result = await node.ExecuteAsync(node.CreateInstance("node", []), CreateContext(provider), CancellationToken.None);

        Assert.Equal(spotify, media.PreferSpotify);
        Assert.Equal(["done"], result.ActivatedPorts);
        Assert.Equal(true, result.Outputs["found"]);
        Assert.Equal("Example song", result.Outputs["title"]);
        Assert.Equal(node.OutputPorts.Where(port => port.Id != "done").Select(port => port.Id), result.Outputs.Keys);
    }

    [Fact]
    public async Task MediaNodeCanReturnAnEmptySnapshotWithoutASystemMediaService()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var node = new GetActiveMediaSessionNode();
        var result = await node.ExecuteAsync(node.CreateInstance("node", []), CreateContext(provider), CancellationToken.None);

        Assert.Equal(false, result.Outputs["found"]);
        Assert.Equal("The system media service is not available", result.Outputs["error"]);
    }

    private static FlowExecutionContext CreateContext(IServiceProvider provider) => new()
    {
        FlowId = "flow",
        ExecutionId = "execution",
        NodeId = "node",
        Inputs = new Dictionary<string, object?>(),
        Config = new Dictionary<string, object?>(),
        Variables = new Dictionary<string, object?>(),
        Services = provider,
        Logger = NullLogger.Instance,
        StartedAt = DateTime.UnixEpoch,
    };

    private sealed class FakeMediaService : ISystemMediaSessionService
    {
        public bool? PreferSpotify { get; private set; }

        public Task<MediaSessionSnapshot> GetSessionAsync(bool preferSpotify, CancellationToken cancellationToken)
        {
            PreferSpotify = preferSpotify;
            return Task.FromResult(new MediaSessionSnapshot(
                true, "example", "Example song", "Example artist", "Example album", "playing", true, 5, 10, 50, ""));
        }
    }
}
