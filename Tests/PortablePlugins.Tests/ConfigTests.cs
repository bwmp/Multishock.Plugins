using System.Reflection;
using MultiShock.PluginSdk;
using OBSIntegration.Services;
using ThroneIntegration.Services;
using VRChatOSC.Services;

namespace PortablePlugins.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("multishock-plugins-");

    [Fact]
    public void ObsLoadsAndUpdatesExistingConfig()
    {
        var host = CreateHost();
        File.WriteAllText(Path.Combine(_temp.FullName, "obs-config.json"),
            """{"Host":"obs.example","Port":4456,"Password":"fixture","AutoConnect":true}""");
        var services = new Services(new FakeSecureStorage());

        var settings = new ObsConfigService(host, services);
        Assert.Equal("obs.example", settings.Host);
        Assert.Equal(4456, settings.Port);
        Assert.Equal("fixture", settings.Password);
        Assert.True(settings.AutoConnect);

        settings.Port = 4457;
        Assert.Equal(4457, new ObsConfigService(host, services).Port);
        Assert.Equal("fixture", new ObsConfigService(host, services).Password);
    }

    [Fact]
    public void ObsMovesSavedPasswordIntoSecureStorage()
    {
        var host = CreateHost();
        var configPath = Path.Combine(_temp.FullName, "obs-config.json");
        File.WriteAllText(configPath, """{"Host":"obs.example","Port":4456,"Password":"fixture"}""");
        var storage = new FakeSecureStorage();

        var settings = new ObsConfigService(host, new Services(storage));

        Assert.Equal("fixture", settings.Password);
        Assert.Null(settings.SecretsWarning);
        Assert.Equal("fixture", storage.Values["com.multishock.obsintegration/password"]);
        Assert.DoesNotContain("fixture", File.ReadAllText(configPath));
        Assert.Equal("obs.example", new ObsConfigService(host, new Services(storage)).Host);

        settings.Password = null;
        Assert.Empty(storage.Values);
    }

    [Fact]
    public void ThroneUsesDefaultsForInvalidJsonAndCanSaveNewSettings()
    {
        var host = CreateHost();
        File.WriteAllText(Path.Combine(_temp.FullName, "throne-config.json"), "{");

        var settings = new ThroneSettingsService(host, new Services(null));
        Assert.False(settings.Enabled);
        Assert.False(settings.HasConnection);
        Assert.Equal(0, settings.Cursor);

        settings.Enabled = true;
        settings.MarkHandled("11111111-1111-4111-8111-111111111111", 42);

        var reloaded = new ThroneSettingsService(host, new Services(null));
        Assert.True(reloaded.Enabled);
        Assert.Equal(42, reloaded.Cursor);
        Assert.True(reloaded.IsHandled("11111111-1111-4111-8111-111111111111"));
    }

    [Fact]
    public void OscUsesDefaultsForMissingConfigAndPreservesClamping()
    {
        var configPath = Path.Combine(_temp.FullName, "nested");
        var host = CreateHost(configPath);
        var settings = new VRChatOscSettingsService(host);
        Assert.Equal("127.0.0.1", settings.ReceiveBindHost);
        Assert.Equal(9000, settings.SendPort);

        settings.SendPort = 70000;

        Assert.Equal(65535, new VRChatOscSettingsService(host).SendPort);
    }

    [Fact]
    public void ThroneMovesSavedTokenIntoSecureStorage()
    {
        var host = CreateHost();
        var configPath = Path.Combine(_temp.FullName, "throne-config.json");
        File.WriteAllText(configPath, LegacyThroneConfig);
        var storage = new FakeSecureStorage();

        var settings = new ThroneSettingsService(host, new Services(storage));

        Assert.True(settings.HasConnection);
        Assert.Equal("legacy-token", settings.ClientToken);
        Assert.Equal("https://relay.example/hook", settings.WebhookUrl);
        Assert.Null(settings.SecretsWarning);
        Assert.Equal("legacy-token", storage.Values["com.multishock.throneintegration/client-token"]);
        Assert.DoesNotContain("legacy-token", File.ReadAllText(configPath));
        Assert.DoesNotContain("relay.example", File.ReadAllText(configPath));

        settings.ClearConnection();
        Assert.Empty(storage.Values);
    }

    [Fact]
    public void ThroneKeepsTokenInPlainFileWithWarningUntilSecureStorageIsAvailable()
    {
        var host = CreateHost();
        var fallbackPath = Path.Combine(_temp.FullName, "plaintext-secrets.json");
        File.WriteAllText(Path.Combine(_temp.FullName, "throne-config.json"), LegacyThroneConfig);

        var oldHost = new ThroneSettingsService(host, new Services(null));
        Assert.Equal("legacy-token", oldHost.ClientToken);
        Assert.NotNull(oldHost.SecretsWarning);
        Assert.Contains("legacy-token", File.ReadAllText(fallbackPath));

        var unavailable = new ThroneSettingsService(host, new Services(new FakeSecureStorage { UnavailableReason = "No keyring." }));
        Assert.StartsWith("No keyring.", unavailable.SecretsWarning);
        Assert.Equal("legacy-token", unavailable.ClientToken);

        var storage = new FakeSecureStorage();
        var available = new ThroneSettingsService(host, new Services(storage));
        Assert.Equal("legacy-token", available.ClientToken);
        Assert.Null(available.SecretsWarning);
        Assert.False(File.Exists(fallbackPath));
        Assert.Equal("legacy-token", storage.Values["com.multishock.throneintegration/client-token"]);
    }

    [Fact]
    public void ThroneKeepsSavedTokenWhenSecureStorageFails()
    {
        var host = CreateHost();
        var configPath = Path.Combine(_temp.FullName, "throne-config.json");
        File.WriteAllText(configPath, LegacyThroneConfig);

        var settings = new ThroneSettingsService(host, new Services(new FakeSecureStorage { FailWrites = true }));

        Assert.Contains("Keyring locked.", settings.SecretsWarning);
        Assert.Contains("legacy-token", File.ReadAllText(configPath));
    }

    private const string LegacyThroneConfig =
        """{"Enabled":true,"ConnectionId":"conn","ClientToken":"legacy-token","WebhookUrl":"https://relay.example/hook"}""";

    private sealed class Services(IPluginSecureStorage? storage) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IPluginSecureStorage) ? storage : null;
    }

    private sealed class FakeSecureStorage : IPluginSecureStorage
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool FailWrites { get; init; }
        public bool IsAvailable => UnavailableReason is null;
        public string? UnavailableReason { get; init; }

        public Task<string?> GetAsync(string pluginId, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault($"{pluginId}/{name}"));

        public Task SetAsync(string pluginId, string name, string value, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new PluginSecureStorageException("Keyring locked.");
            }

            Values[$"{pluginId}/{name}"] = value;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string pluginId, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.Remove($"{pluginId}/{name}"));
    }

    private IPluginHost CreateHost(string? directory = null)
    {
        var host = DispatchProxy.Create<IPluginHost, PluginDataHost>();
        ((PluginDataHost)(object)host).Directory = directory ?? _temp.FullName;
        return host;
    }

    public void Dispose() => _temp.Delete(recursive: true);

    public class PluginDataHost : DispatchProxy
    {
        public string Directory { get; set; } = "";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IPluginHost.GetPluginDataPath)
                ? Directory
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
