using SonicScrewdriver.Input;
using Windows.Media.Control;

namespace SonicScrewdriver.Media;

/// <summary>Sends transport commands to a specific app via Global System Media Transport Controls.</summary>
public sealed class MediaSessionService : IMediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<MediaResult> SendAsync(string exe, MediaKey key)
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = MediaSessionMatcher.PreferPlaying(
                _manager.GetSessions().Where(s => MediaSessionMatcher.Matches(s.SourceAppUserModelId, exe)),
                s => { try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch (Exception) { return false; } });
            if (session is null) return MediaResult.NoSession;
            var ok = key switch
            {
                MediaKey.PlayPause => await session.TryTogglePlayPauseAsync(),
                MediaKey.Next => await session.TrySkipNextAsync(),
                MediaKey.Previous => await session.TrySkipPreviousAsync(),
                _ => false,
            };
            return ok ? MediaResult.Ok : MediaResult.Failed;
        }
        catch (Exception)
        {
            return MediaResult.Failed;
        }
    }
}
