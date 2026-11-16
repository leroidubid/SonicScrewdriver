using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Audio;

/// <summary>What applying a profile did: apps set, apps that are not playing (skipped), and whether the master was set.</summary>
public readonly record struct ProfileResult(int Applied, int Missing, bool MasterSet);

/// <summary>Profiles started by a program are applied again a few seconds later (the app opens its audio session late).</summary>
public sealed class ProfileRetry
{
    private readonly List<VolumeProfile> _profiles = new();

    public bool Pending => _profiles.Count > 0;
    public void Add(VolumeProfile profile) => _profiles.Add(profile);
    public void Clear() => _profiles.Clear();

    /// <summary>Applies the waiting profiles once and forgets them; nothing is written while <paramref name="blocked"/> says so (competitive mode).</summary>
    public void Run(IAudioSessionService audio, Func<bool>? blocked = null)
    {
        var profiles = _profiles.ToList();
        _profiles.Clear();
        if (blocked?.Invoke() == true) return;
        foreach (var p in profiles) ProfileApplier.Apply(p, audio);
    }
}

public static class ProfileApplier
{
    /// <summary>
    /// Sets each app of the profile that has a session (the others are skipped: Windows has nothing to set until they
    /// play) and the master volume when the profile has one. Like the shortcuts, a volume above zero unmutes the app.
    /// The maximum volumes still apply (the audio service clamps).
    /// </summary>
    public static ProfileResult Apply(VolumeProfile profile, IAudioSessionService audio)
    {
        int applied = 0, missing = 0;
        foreach (var app in profile.Apps)
        {
            if (!audio.SetVolume(app.ExeName, app.Volume / 100f)) { missing++; continue; }
            applied++;
            if (app.Volume > 0 && audio.GetMute(app.ExeName) == true) audio.SetMute(app.ExeName, false);
        }
        var master = profile.MasterVolume is { } m;
        if (master) audio.MasterVolume = profile.MasterVolume!.Value / 100f;
        return new ProfileResult(applied, missing, master);
    }
}
