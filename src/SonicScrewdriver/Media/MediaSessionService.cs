using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;
using Windows.Media.Control;

namespace SonicScrewdriver.Media;

/// <summary>Sends transport commands to a specific app via Global System Media Transport Controls.</summary>
public sealed class MediaSessionService : IMediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Action? _onChanged;
    private readonly List<GlobalSystemMediaTransportControlsSession> _watched = new();

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch (Exception) { return false; }
    }

    public async Task<NowPlaying?> GetNowPlayingAsync(string exe)
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = MediaSessionMatcher.PreferPlaying(
                _manager.GetSessions().Where(s => MediaSessionMatcher.Matches(s.SourceAppUserModelId, exe)), IsPlaying);
            if (session is null) return null;
            var props = await session.TryGetMediaPropertiesAsync();
            var title = (props.Title ?? "").Trim();
            var artist = (props.Artist ?? "").Trim();
            return title.Length == 0 && artist.Length == 0 ? null : new NowPlaying(title.Length == 0 ? artist : title, title.Length == 0 ? "" : artist, IsPlaying(session));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async void Watch(Action? onChanged)
    {
        _onChanged = onChanged;
        try
        {
            if (onChanged is null) { Unwatch(); return; }
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_onChanged is null) return; // stopped while the manager was loading
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            Resubscribe();
        }
        catch (Exception) { /* no media sessions available: nothing to watch */ }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        // Windows thread-pool thread: a session that vanishes mid-walk must not end the process.
        ErrorLog.Default.Guard("Media sessions", () =>
        {
            if (_onChanged is null) return;
            Resubscribe();
            _onChanged?.Invoke();
        });
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) =>
        ErrorLog.Default.Guard("Media session", () => _onChanged?.Invoke());

    private void Resubscribe()
    {
        lock (_watched)
        {
            Release();
            if (_manager is null || _onChanged is null) return;
            foreach (var s in _manager.GetSessions())
            {
                s.MediaPropertiesChanged += OnSessionChanged;
                s.PlaybackInfoChanged += OnSessionChanged;
                _watched.Add(s);
            }
        }
    }

    private void Release()
    {
        foreach (var s in _watched)
        {
            try { s.MediaPropertiesChanged -= OnSessionChanged; s.PlaybackInfoChanged -= OnSessionChanged; } catch (Exception) { }
        }
        _watched.Clear();
    }

    private void Unwatch()
    {
        if (_manager is not null) _manager.SessionsChanged -= OnSessionsChanged;
        lock (_watched) Release();
    }

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
