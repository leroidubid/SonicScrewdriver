using SonicScrewdriver.Input;

namespace SonicScrewdriver.Media;

public enum MediaResult
{
    Ok,
    NoSession,
    Failed,
}

/// <summary>What an app is playing, from its media session.</summary>
public sealed record NowPlaying(string Title, string Artist, bool IsPlaying)
{
    /// <summary>"Artist — Title", or just the title when there is no artist.</summary>
    public string Text => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";
}

public interface IMediaSessionService
{
    Task<MediaResult> SendAsync(string exe, MediaKey key);

    /// <summary>The title and artist of the app's media session (the playing one when it has several); null when it has none.</summary>
    Task<NowPlaying?> GetNowPlayingAsync(string exe);

    /// <summary>
    /// Calls <paramref name="onChanged"/> (any thread) when a media session starts, ends, changes track or pauses; null stops
    /// watching and releases every subscription. Meant to be on only while the mixer is open.
    /// </summary>
    void Watch(Action? onChanged);
}
