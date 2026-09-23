using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk;
using WebSocketControl.Services;

namespace WebSocketControl.Tests;

public sealed class WebSocketControlTests
{
    [Fact]
    public async Task LegacyCommandsAuthenticateAndTargetTheRequestedShockers()
    {
        using var temp = new TemporaryDirectory();
        var host = DispatchProxy.Create<IPluginHost, HostProxy>();
        ((HostProxy)host).DataPath = temp.Path;
        var devices = DispatchProxy.Create<IDeviceActions, DeviceProxy>();
        var proxy = (DeviceProxy)devices;
        proxy.Shockers =
        [
            new ShockerInfo { DeviceId = 10, ShockerId = 1, Name = "Left" },
            new ShockerInfo { DeviceId = 10, ShockerId = 2, Name = "Right" },
            new ShockerInfo { DeviceId = 20, ShockerId = 3, Name = "Other" }
        ];
        var settings = new WebSocketControlSettings(host);
        var port = GetFreePort();
        settings.Update(true, port, "test-auth-key");
        var reloadedSettings = new WebSocketControlSettings(host);
        Assert.Equal(port, reloadedSettings.Port);
        Assert.Equal("test-auth-key", reloadedSettings.AuthKey);
        using var server = new WebSocketControlServer(settings, devices, host);
        Assert.True(server.Start(), server.LastError);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://localhost:{port}/"), CancellationToken.None);

        await SendAsync(client, """{"cmd":"get_devices","auth_key":"wrong"}""");
        using (var error = JsonDocument.Parse(await ReceiveAsync(client)))
            Assert.Equal("unauthorized", error.RootElement.GetProperty("value").GetProperty("code").GetString());

        await SendAsync(client, """{"cmd":"get_devices","auth_key":"test-auth-key"}""");
        using (var list = JsonDocument.Parse(await ReceiveAsync(client)))
        {
            Assert.Equal(2, list.RootElement.GetArrayLength());
            Assert.Equal(10, list.RootElement[0].GetProperty("id").GetInt32());
            Assert.Equal(1, list.RootElement[0].GetProperty("shockers")[0].GetProperty("identifier").GetInt32());
        }

        await SendAsync(client, """{"cmd":"operate","auth_key":"test-auth-key","value":{"action":"vibrate","intensity":25,"duration_ms":500,"device_ids":[10],"shocker_ids":[3]}}""");
        Assert.True(SpinWait.SpinUntil(() => proxy.Calls.Count == 2, TimeSpan.FromSeconds(3)));
        var calls = proxy.Calls.OrderBy(call => call.DeviceId).ToArray();
        Assert.Equal([1, 2], calls[0].ShockerIds);
        Assert.Equal([3], calls[1].ShockerIds);
        Assert.All(calls, call =>
        {
            Assert.Equal(CommandType.Vibrate, call.Command);
            Assert.Equal(25, call.Intensity);
            Assert.Equal(0.5, call.Duration);
        });

        await SendAsync(client, """{"cmd":"operate","auth_key":"test-auth-key","value":{"action":"shock","group_ids":[0]}}""");
        using (var error = JsonDocument.Parse(await ReceiveAsync(client)))
            Assert.Equal("unsupported_groups", error.RootElement.GetProperty("value").GetProperty("code").GetString());
        Assert.Equal(2, proxy.Calls.Count);

        await SendAsync(client, """{"cmd":"operate","auth_key":"test-auth-key","value":{"action":"end_now","shocker_ids":[1]}}""");
        Assert.True(SpinWait.SpinUntil(() => proxy.Calls.Count == 3, TimeSpan.FromSeconds(3)));
        Assert.Equal(CommandType.End, proxy.Calls.Last().Command);

        var legacyMessage = JsonSerializer.Serialize("""{"cmd":"operate","auth_key":"test-auth-key","value":{"action":"beep","intensity":12,"duration":500,"shocker_ids":["2"]}}""");
        await SendAsync(client, legacyMessage);
        Assert.True(SpinWait.SpinUntil(() => proxy.Calls.Count == 4, TimeSpan.FromSeconds(3)));
        var last = proxy.Calls.Last();
        Assert.Equal(CommandType.Beep, last.Command);
        Assert.Equal(0.5, last.Duration);
        Assert.Equal([2], last.ShockerIds);

    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Task SendAsync(ClientWebSocket socket, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
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

    public class DeviceProxy : DispatchProxy
    {
        public ShockerInfo[] Shockers { get; set; } = [];
        public ConcurrentQueue<ActionCall> Calls { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "GetLoadedShockers":
                    return Shockers;
                case "GetLoadedShockersGroupedByDevice":
                    return Shockers.GroupBy(shocker => shocker.DeviceId)
                        .Select(group => new DeviceShockerGroup
                        {
                            DeviceId = group.Key,
                            DeviceName = $"Device {group.Key}",
                            Shockers = group.ToArray()
                        }).ToArray();
                case "PerformAction":
                    Calls.Enqueue(new ActionCall((int)args![0]!, (double)args[1]!, (CommandType)args[2]!,
                        ((IEnumerable<int>)args[4]!).Single(), ((IEnumerable<int>)args[5]!).ToArray()));
                    return null;
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    public sealed record ActionCall(int Intensity, double Duration, CommandType Command, int DeviceId, int[] ShockerIds);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "multishock-ws-test-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
