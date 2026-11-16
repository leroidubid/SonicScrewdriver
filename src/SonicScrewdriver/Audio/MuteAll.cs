namespace SonicScrewdriver.Audio;

/// <summary>
/// "Mute everything": the first call mutes every app that is not muted yet (per-app mute: the Windows master volume
/// and the volume levels are never touched), the second restores each app's previous mute state exactly.
/// </summary>
public sealed class MuteAll
{
    private readonly IAudioSessionService _audio;
    private Dictionary<(string Device, string Exe), bool>? _saved;

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
        // Per device: an app with sessions on two outputs keeps the mute state each one had.
        var saved = new Dictionary<(string, string), bool>(new DeviceExeComparer());
        foreach (var device in _audio.GetDevices())
            foreach (var app in device.Apps)
            {
                if (Excluded.Contains(app.ExeName, StringComparer.OrdinalIgnoreCase)) continue;
                saved[(device.Id, app.ExeName)] = app.Muted;
                if (!app.Muted) _audio.SetMute(app.ExeName, true, device.Id);
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
        foreach (var ((device, exe), wasMuted) in saved)
            if (!wasMuted) _audio.SetMute(exe, false, device);
    }

    private sealed class DeviceExeComparer : IEqualityComparer<(string Device, string Exe)>
    {
        public bool Equals((string Device, string Exe) a, (string Device, string Exe) b) =>
            a.Device == b.Device && string.Equals(a.Exe, b.Exe, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Device, string Exe) k) => HashCode.Combine(k.Device, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Exe));
    }
}
