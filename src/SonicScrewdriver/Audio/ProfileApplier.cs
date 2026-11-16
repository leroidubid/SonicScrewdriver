using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Audio;

/// <summary>What applying a profile did: apps set, apps that are not playing (skipped), and whether the master was set.</summary>
public readonly record struct ProfileResult(int Applied, int Missing, bool MasterSet);

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
