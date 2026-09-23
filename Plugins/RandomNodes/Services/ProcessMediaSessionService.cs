using System.Diagnostics;
using System.Globalization;

namespace RandomNodes.Services;

public sealed class ProcessMediaSessionService : ISystemMediaSessionService
{
    public async Task<MediaSessionSnapshot> GetSessionAsync(bool preferSpotify, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            return await GetMacSessionAsync(preferSpotify, cancellationToken).ConfigureAwait(false);
        }

        if (OperatingSystem.IsLinux())
        {
            return await GetLinuxSessionAsync(preferSpotify, cancellationToken).ConfigureAwait(false);
        }

        return MediaSessionSnapshot.Empty("Media sessions are not supported on this platform.");
    }

    private static async Task<MediaSessionSnapshot> GetMacSessionAsync(bool preferSpotify, CancellationToken cancellationToken)
    {
        var applications = preferSpotify ? new[] { "Spotify" } : new[] { "Spotify", "Music" };
        foreach (var application in applications)
        {
            var script = application == "Spotify"
                ? "tell application \"Spotify\" to if it is running then return (name of current track) & linefeed & (artist of current track) & linefeed & (album of current track) & linefeed & (player state as text) & linefeed & (player position as text) & linefeed & (duration of current track as text)"
                : "tell application \"Music\" to if it is running then return (name of current track) & linefeed & (artist of current track) & linefeed & (album of current track) & linefeed & (player state as text) & linefeed & (player position as text) & linefeed & ((duration of current track) as text)";
            var result = await RunAsync("osascript", ["-e", script], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            {
                continue;
            }

            var values = result.Output.Replace("\r", string.Empty).Split('\n');
            if (values.Length < 6)
            {
                continue;
            }

            var length = ParseDouble(values[5]);
            if (application == "Spotify")
            {
                length /= 1000d;
            }

            return Snapshot(
                application,
                values[0],
                values[1],
                values[2],
                values[3],
                ParseDouble(values[4]),
                length);
        }

        return MediaSessionSnapshot.Empty("No active Spotify or Music session was found.");
    }

    private static async Task<MediaSessionSnapshot> GetLinuxSessionAsync(bool preferSpotify, CancellationToken cancellationToken)
    {
        const string format = "{{playerName}}\\n{{title}}\\n{{artist}}\\n{{album}}\\n{{status}}\\n{{position}}\\n{{mpris:length}}";
        var arguments = preferSpotify
            ? new[] { "--player=spotify", "metadata", "--format", format }
            : new[] { "metadata", "--format", format };
        var result = await RunAsync("playerctl", arguments, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0 && preferSpotify)
        {
            result = await RunAsync("playerctl", ["metadata", "--format", format], cancellationToken).ConfigureAwait(false);
        }

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.Error)
                ? "No MPRIS media session was found. Install playerctl to enable Linux media nodes."
                : result.Error.Trim();
            return MediaSessionSnapshot.Empty(error);
        }

        var values = result.Output.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\r", string.Empty).Split('\n');
        if (values.Length < 7)
        {
            return MediaSessionSnapshot.Empty("The MPRIS player returned incomplete metadata.");
        }

        return Snapshot(
            values[0],
            values[1],
            values[2],
            values[3],
            values[4],
            ParseDouble(values[5]) / 1_000_000d,
            ParseDouble(values[6]) / 1_000_000d);
    }

    private static MediaSessionSnapshot Snapshot(
        string source,
        string title,
        string artist,
        string album,
        string status,
        double current,
        double length) => new(
            true,
            source,
            title,
            artist,
            album,
            status,
            status.Equals("playing", StringComparison.OrdinalIgnoreCase),
            current,
            length,
            length > 0 ? Math.Clamp(current / length * 100, 0, 100) : 0,
            string.Empty);

    private static double ParseDouble(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(-1, string.Empty, $"Could not start {fileName}.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
