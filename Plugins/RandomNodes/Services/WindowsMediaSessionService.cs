using Windows.Media.Control;

namespace RandomNodes.Services;

public sealed record MediaSessionSnapshot(
    bool Found,
    string SourceAppUserModelId,
    string Title,
    string Artist,
    string AlbumTitle,
    string PlaybackStatus,
    bool IsPlaying,
    double CurrentSeconds,
    double LengthSeconds,
    double ProgressPercent,
    string Error);

public sealed class WindowsMediaSessionService
{
    public async Task<MediaSessionSnapshot> GetSessionAsync(bool preferSpotify, CancellationToken cancellationToken)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var session = preferSpotify
                ? manager.GetSessions().FirstOrDefault(IsSpotifySession) ?? manager.GetCurrentSession()
                : manager.GetCurrentSession();

            if (session is null)
            {
                return Empty("No active Windows media session was found");
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();

            var currentSeconds = Math.Max(0, timeline.Position.TotalSeconds);
            var lengthSeconds = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
            var progressPercent = lengthSeconds > 0 ? Math.Clamp(currentSeconds / lengthSeconds * 100, 0, 100) : 0;
            var status = playback.PlaybackStatus.ToString();

            return new MediaSessionSnapshot(
                true,
                session.SourceAppUserModelId ?? string.Empty,
                properties.Title ?? string.Empty,
                properties.Artist ?? string.Empty,
                properties.AlbumTitle ?? string.Empty,
                status,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                currentSeconds,
                lengthSeconds,
                progressPercent,
                string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Empty(ex.Message);
        }
    }

    private static bool IsSpotifySession(GlobalSystemMediaTransportControlsSession session)
    {
        return session.SourceAppUserModelId?.Contains("spotify", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static MediaSessionSnapshot Empty(string error) => new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        0,
        0,
        0,
        error);
}
