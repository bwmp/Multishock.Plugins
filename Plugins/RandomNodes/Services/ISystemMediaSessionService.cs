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
    string Error)
{
    public static MediaSessionSnapshot Empty(string error) => new(
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

public interface ISystemMediaSessionService
{
    Task<MediaSessionSnapshot> GetSessionAsync(bool preferSpotify, CancellationToken cancellationToken);
}
