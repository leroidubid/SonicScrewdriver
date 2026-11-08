namespace SonicScrewdriver.Audio;

public sealed record AudioApp(string ExeName, string DisplayName, int ProcessId, float Volume, bool Muted);

public sealed record AudioDevice(string Id, string Name, bool IsDefault, float Volume, bool Muted, IReadOnlyList<AudioApp> Apps);

/// <summary>
/// Per-app volume across all active output devices. Exe-based calls act on every session of that exe
/// (optionally limited to one device). Null/false means "no session for this exe".
/// </summary>
public interface IAudioSessionService
{
    /// <summary>Apps playing on any device, merged by exe.</summary>
    IReadOnlyList<AudioApp> GetApps();
    IReadOnlyList<AudioDevice> GetDevices();
    IReadOnlyList<int> GetProcessIds(string exe);

    float? GetVolume(string exe);
    bool SetVolume(string exe, float volume, string? deviceId = null);
    bool? GetMute(string exe);
    bool? ToggleMute(string exe);
    bool SetMute(string exe, bool muted, string? deviceId = null);

    /// <summary>Default output device.</summary>
    float MasterVolume { get; set; }
    bool MasterMuted { get; set; }
    void SetDeviceVolume(string deviceId, float volume);
    void SetDeviceMute(string deviceId, bool muted);
    bool SetDefaultDevice(string deviceId);

    /// <summary>Sessions or devices appeared/disappeared. Raised on any thread.</summary>
    event EventHandler? SessionsChanged;
    /// <summary>A volume or mute changed (from SonicScrewdriver or elsewhere). Raised on any thread.</summary>
    event EventHandler? VolumesChanged;

    string GetDisplayName(string exe);
}
