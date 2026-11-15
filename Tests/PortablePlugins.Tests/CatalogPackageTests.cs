using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MultiShock.PluginSdk;

namespace PortablePlugins.Tests;

public sealed class CatalogPackageTests
{
    private const string WindowsPackagePathVariable = "RANDOM_NODES_WINDOWS_PACKAGE";

    [WindowsPackageFact]
    public void WindowsBuiltRandomNodesPackageCanUseThePortableMediaBackend()
    {
        using var archive = ZipFile.OpenRead(Environment.GetEnvironmentVariable(WindowsPackagePathVariable)!);
        using var manifest = JsonDocument.Parse(archive.GetEntry("plugin.json")!.Open());
        var metadata = manifest.RootElement;
        Assert.Equal("com.multishock.randomnodes", metadata.GetProperty("id").GetString());
        Assert.Contains(metadata.GetProperty("platforms").EnumerateArray(), platform => platform.GetString() == "linux-x64");

        var entryPoint = metadata.GetProperty("entryPoint").GetString()!;
        var context = new AssemblyLoadContext("Windows-built RandomNodes package", isCollectible: true);
        // The production host resolves managed dependencies bundled alongside the entry assembly.
        context.Resolving += (loadContext, name) => LoadAssembly(loadContext, archive.GetEntry($"{name.Name}.dll"));
        try
        {
            var assembly = LoadAssembly(context, archive.GetEntry(entryPoint))!;
            var pluginType = Assert.Single(assembly.GetTypes(), type => !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type));
            var plugin = Assert.IsAssignableFrom<IPlugin>(Activator.CreateInstance(pluginType));

            var services = new ServiceCollection();
            plugin.ConfigureServices(services);
            using var provider = services.BuildServiceProvider();

            // Confirm this really contains the Windows backend but does not select it on Linux.
            Assert.NotNull(assembly.GetType("RandomNodes.Services.WindowsMediaSessionService"));
            var serviceType = assembly.GetType("RandomNodes.Services.ISystemMediaSessionService", throwOnError: true)!;
            var expected = OperatingSystem.IsWindows() ? "WindowsMediaSessionService" : "ProcessMediaSessionService";
            Assert.Equal(expected, provider.GetRequiredService(serviceType).GetType().Name);
        }
        finally
        {
            context.Unload();
        }
    }

    private static System.Reflection.Assembly? LoadAssembly(AssemblyLoadContext context, ZipArchiveEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        using var source = entry.Open();
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        bytes.Position = 0;
        return context.LoadFromStream(bytes);
    }

    public sealed class WindowsPackageFactAttribute : FactAttribute
    {
        public WindowsPackageFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WindowsPackagePathVariable)))
            {
                Skip = $"Set {WindowsPackagePathVariable} to a Windows-built RandomNodes.msplugin.";
            }
        }
    }
}
