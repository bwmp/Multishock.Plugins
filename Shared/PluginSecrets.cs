using System.Runtime.CompilerServices;
using System.Text.Json;
using MultiShock.PluginSdk;

namespace MultiShock.Plugins.Shared;

/// <summary>
/// A plugin's credentials, kept in the host's secure storage. When the host can't store them securely,
/// they go to a plain file in the plugin's data folder instead and <see cref="Warning"/> says so, so the
/// plugin can show it. Values are cached after the first read.
/// </summary>
// Linked into each consuming plugin so packages do not need a shared runtime assembly.
internal sealed class PluginSecrets
{
    private const string FallbackFileName = "plaintext-secrets.json";

    private readonly string _pluginId;
    private readonly string _fallbackPath;
    // Really an IPluginSecureStorage. Typed as object, and only touched through SecureStorageCalls, so this class
    // still loads on hosts built before SDK 1.17.0, where that type doesn't exist.
    private readonly object? _storage;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly string? _insecureWarning;
    private string? _readError;

    public PluginSecrets(IServiceProvider services, IPluginHost pluginHost, string pluginId)
    {
        _pluginId = pluginId;
        _fallbackPath = Path.Combine(pluginHost.GetPluginDataPath(pluginId), FallbackFileName);

        var storage = ResolveSecureStorage(services);
        if (storage is null)
        {
            _insecureWarning = "This version of MultiShock can't store credentials securely, so they're saved in a plain file. Update MultiShock to fix this.";
        }
        else if (SecureStorageCalls.UnavailableReason(storage) is { } reason)
        {
            _insecureWarning = $"{reason} Until then, credentials are saved in a plain file.";
        }
        else
        {
            _storage = storage;
            MoveFallbackFileToSecureStorage();
        }
    }

    /// <summary>Why credentials aren't safely stored or couldn't be read, or null when everything is fine.</summary>
    public string? Warning => _readError ?? _insecureWarning;

    public string? Get(string name)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            string? value;
            try
            {
                value = _storage is null ? ReadFallbackFile().GetValueOrDefault(name) : SecureStorageCalls.Get(_storage, _pluginId, name);
            }
            // Getters are read while rendering; record the failure for Warning instead of breaking the page.
            // It's cached as missing so a dismissed unlock prompt doesn't reappear on every read.
            catch (Exception ex) when (IsStoreFailure(ex))
            {
                _readError = $"Couldn't read saved credentials: {ex.Message}";
                value = null;
            }

            _cache[name] = value;
            return value;
        }
    }

    /// <summary>Stores a value, or removes it when <paramref name="value"/> is null or empty. Throws if it can't be saved.</summary>
    public void Set(string name, string? value)
    {
        lock (_lock)
        {
            // Some pages save on every keystroke; skip writes that change nothing.
            if (_readError is null && _cache.TryGetValue(name, out var cached) &&
                (cached ?? "") == (value ?? ""))
            {
                return;
            }

            if (string.IsNullOrEmpty(value))
            {
                Remove(name);
                return;
            }

            if (_storage is null)
            {
                var values = ReadFallbackFile();
                values[name] = value;
                WriteFallbackFile(values);
            }
            else
            {
                SecureStorageCalls.Set(_storage, _pluginId, name, value);
            }

            _cache[name] = value;
            _readError = null;
        }
    }

    /// <summary>
    /// Moves a credential read from an older plain config file into these secrets, unless one is already stored.
    /// Returns true when the caller can clear the old copy.
    /// </summary>
    public bool Import(string name, string? legacyValue)
    {
        if (string.IsNullOrEmpty(legacyValue))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(Get(name)))
        {
            return true;
        }

        try
        {
            Set(name, legacyValue);
            return true;
        }
        // Keep the old copy so the credential isn't lost, and report why it couldn't move.
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            lock (_lock)
            {
                _readError = $"Couldn't move saved credentials to secure storage: {ex.Message}";
                _cache.Remove(name);
            }

            return false;
        }
    }

    private void Remove(string name)
    {
        if (_storage is null)
        {
            var values = ReadFallbackFile();
            if (values.Remove(name))
            {
                WriteFallbackFile(values);
            }
        }
        else
        {
            SecureStorageCalls.Remove(_storage, _pluginId, name);
        }

        _cache[name] = null;
        _readError = null;
    }

    // Runs when secure storage has become available after credentials were saved in the plain file.
    private void MoveFallbackFileToSecureStorage()
    {
        if (!File.Exists(_fallbackPath))
        {
            return;
        }

        try
        {
            foreach (var (name, value) in ReadFallbackFile())
            {
                if (string.IsNullOrEmpty(SecureStorageCalls.Get(_storage!, _pluginId, name)))
                {
                    SecureStorageCalls.Set(_storage!, _pluginId, name, value);
                }
            }

            File.Delete(_fallbackPath);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            _readError = $"Couldn't move saved credentials to secure storage: {ex.Message}";
        }
    }

    private bool IsStoreFailure(Exception ex) =>
        ex is IOException or JsonException or UnauthorizedAccessException ||
        (_storage is not null && SecureStorageCalls.IsStoreFailure(ex));

    private Dictionary<string, string> ReadFallbackFile() =>
        File.Exists(_fallbackPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_fallbackPath)) ?? []
            : [];

    private void WriteFallbackFile(Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            File.Delete(_fallbackPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_fallbackPath)!);
        var temporaryPath = _fallbackPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(values));
        File.Move(temporaryPath, _fallbackPath, overwrite: true);
    }

    private static object? ResolveSecureStorage(IServiceProvider services)
    {
        try
        {
            return SecureStorageCalls.Resolve(services);
        }
        // Hosts built before SDK 1.17.0 don't have IPluginSecureStorage at all.
        catch (TypeLoadException)
        {
            return null;
        }
    }

    // Every reference to IPluginSecureStorage lives here; these methods are only compiled once called.
    private static class SecureStorageCalls
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object? Resolve(IServiceProvider services) => services.GetService(typeof(IPluginSecureStorage));

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsStoreFailure(Exception ex) => ex is PluginSecureStorageException;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string? UnavailableReason(object storage)
        {
            var secureStorage = (IPluginSecureStorage)storage;
            return secureStorage.IsAvailable ? null : secureStorage.UnavailableReason ?? "Secure storage isn't available.";
        }

        // The host runs these on the thread pool, so blocking here can't deadlock.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string? Get(object storage, string pluginId, string name) =>
            ((IPluginSecureStorage)storage).GetAsync(pluginId, name).GetAwaiter().GetResult();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Set(object storage, string pluginId, string name, string value) =>
            ((IPluginSecureStorage)storage).SetAsync(pluginId, name, value).GetAwaiter().GetResult();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Remove(object storage, string pluginId, string name) =>
            ((IPluginSecureStorage)storage).RemoveAsync(pluginId, name).GetAwaiter().GetResult();
    }
}
