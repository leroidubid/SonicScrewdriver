namespace SonicScrewdriver.Audio;

/// <summary>
/// "Mute everything": the first call mutes every app that is not muted yet (per-app mute: the Windows master volume
/// and the volume levels are never touched), the second restores each app's previous mute state exactly.
/// </summary>
public sealed class MuteAll
{
    private readonly IAudioSessionService _audio;
    private Dictionary<string, bool>? _saved;

    public MuteAll(IAudioSessionService audio) => _audio = audio;

    /// <summary>True between the mute and the restore.</summary>
    public bool Active => _saved is not null;

    /// <summary>Apps that stay out of it (hidden apps keep working: their volume is not touched).</summary>
    public IReadOnlyCollection<string> Excluded { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Mutes everything, or restores. Returns null when there was nothing to mute, otherwise whether everything is
    /// muted now (true) or was restored (false).
    /// </summary>
    public bool? Toggle()
    {
        if (_saved is not null)
        {
            Restore();
            return false;
        }
        var saved = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in _audio.GetApps())
        {
            if (Excluded.Contains(app.ExeName, StringComparer.OrdinalIgnoreCase)) continue;
            saved[app.ExeName] = app.Muted;
            if (!app.Muted) _audio.SetMute(app.ExeName, true);
        }
        if (saved.Count == 0) return null;
        _saved = saved;
        return true;
    }

    /// <summary>Puts every app back as it was; apps that are gone are skipped, apps that started meanwhile are left alone. No-op when not active.</summary>
    public void Restore()
    {
        if (_saved is null) return;
        var saved = _saved;
        _saved = null;
        foreach (var (exe, wasMuted) in saved)
            if (!wasMuted) _audio.SetMute(exe, false);
    }
}
