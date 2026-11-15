using System.Text.Json;

namespace MultiShock.Plugins.Shared;

// Linked into each consuming plugin so packages do not need a shared runtime assembly.
internal static class PluginConfigFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static T LoadOrDefault<T>(string path) where T : class, new()
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
            }
        }
        catch
        {
            // Invalid or inaccessible config falls back to defaults.
        }

        return new T();
    }

    public static void Save<T>(string path, T state)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(state, WriteOptions));
        }
        catch
        {
            // Preserve the plugins' best-effort persistence behavior.
        }
    }
}
