using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.VrWatch;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Services;

/// <summary>
/// Collects the cards and pages other plugins add to the watch through
/// <see cref="IVrWatchProvider"/>, and hands their controls' inputs back to them. A plugin that
/// throws or misbehaves only loses its own content; the watch keeps running.
/// </summary>
public sealed class WatchProviderService : IDisposable
{
    private readonly IPluginHost? _host;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Provider> _providers = new(StringComparer.Ordinal);

    /// <summary>Plugins whose last <see cref="IVrWatchProvider.GetWatchContent"/> threw, so each failure is logged once.</summary>
    private readonly HashSet<string> _failing = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<WatchInputResult> _results = new();
    private readonly CancellationTokenSource _cts = new();
    private IReadOnlyList<WatchContribution> _last = [];

    public WatchProviderService(IServiceProvider services)
    {
        _host = services.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = _host?.CreateLogger("SteamVROverlay.WatchProviders");
    }

    /// <summary>
    /// Reads every provider's current content. Safe to call from any thread.
    /// </summary>
    public IReadOnlyList<WatchContribution> Poll()
    {
        if (_host is null)
        {
            return [];
        }

        IPlugin[] plugins;
        try
        {
            plugins = [.. _host.GetLoadedPlugins()];
        }
        catch (InvalidOperationException)
        {
            // The host's list changed while a plugin was loading or unloading; try again next poll.
            return _last;
        }

        var contributions = new List<WatchContribution>();
        var providers = new Dictionary<string, Provider>(StringComparer.Ordinal);

        foreach (var plugin in plugins)
        {
            if (plugin is not IVrWatchProvider provider)
            {
                continue;
            }

            providers[plugin.Id] = new Provider(plugin.Id, plugin.Name, provider);
            if (Read(plugin, provider) is { } content)
            {
                contributions.Add(new WatchContribution(plugin.Id, plugin.Name, content));
            }
        }

        lock (_gate)
        {
            // Dropping unloaded plugins releases them, so a hot-reloaded plugin's old assembly can unload.
            foreach (var removed in _providers.Keys.Except(providers.Keys).ToList())
            {
                if (_providers[removed].Pending == 0)
                {
                    _providers.Remove(removed);
                }
            }

            foreach (var (id, provider) in providers)
            {
                if (_providers.TryGetValue(id, out var existing) && ReferenceEquals(existing.Instance, provider.Instance))
                {
                    continue;
                }

                _providers[id] = provider;
            }
        }

        _last = contributions;
        return contributions;
    }

    private VrWatchContent? Read(IPlugin plugin, IVrWatchProvider provider)
    {
        VrWatchContent? content;
        try
        {
            content = provider.GetWatchContent();
        }
        catch (Exception ex)
        {
            bool firstFailure;
            lock (_gate)
            {
                firstFailure = _failing.Add(plugin.Id);
            }

            if (firstFailure)
            {
                _logger?.LogWarning(ex, "{Plugin} failed to provide its wrist watch content; it is left off the watch until it succeeds", plugin.Name);
            }

            return null;
        }

        lock (_gate)
        {
            _failing.Remove(plugin.Id);
        }

        return content is null ? null : WatchContent.Normalize(content);
    }

    /// <summary>
    /// Sends an input to the plugin that owns the control. Inputs to one plugin run one at a
    /// time, in order, on the thread pool; the outcome arrives through <see cref="TryTakeResult"/>.
    /// </summary>
    public void Dispatch(WatchInputRequest request)
    {
        lock (_gate)
        {
            if (!_providers.TryGetValue(request.PluginId, out var provider))
            {
                _results.Enqueue(new WatchInputResult(request.PluginId, "That plugin is no longer loaded", Settled: true));
                return;
            }

            provider.Pending++;
            provider.Tail = provider.Tail
                .ContinueWith(_ => RunAsync(provider, request.Input), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task RunAsync(Provider provider, VrWatchInput input)
    {
        string? message;
        try
        {
            message = await provider.Instance.OnWatchInputAsync(input, _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "{Plugin} failed to handle {Input} from the wrist watch", provider.Name, input);
            message = $"{provider.Name} could not do that";
        }

        bool settled;
        lock (_gate)
        {
            settled = --provider.Pending == 0;
        }

        _results.Enqueue(new WatchInputResult(provider.PluginId, message, settled));
    }

    public bool TryTakeResult([MaybeNullWhen(false)] out WatchInputResult result) => _results.TryDequeue(out result);

    /// <summary>
    /// Cancels inputs still being handled. The token source is not disposed, since a queued
    /// input may still start and read its token.
    /// </summary>
    public void Dispose() => _cts.Cancel();

    private sealed class Provider(string pluginId, string name, IVrWatchProvider instance)
    {
        public string PluginId { get; } = pluginId;

        public string Name { get; } = name;

        public IVrWatchProvider Instance { get; } = instance;

        /// <summary>Inputs sent and not yet handled. Guarded by the service's lock.</summary>
        public int Pending { get; set; }

        /// <summary>The last input queued, which the next one waits for. Guarded by the service's lock.</summary>
        public Task Tail { get; set; } = Task.CompletedTask;
    }
}
