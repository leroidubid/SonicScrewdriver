using SonicScrewdriver.Bindings;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Audio;

/// <summary>The maximum volume of some apps (0..1 by exe name). Swapped as a whole, so it can be read from any thread.</summary>
public sealed class VolumeLimits
{
    private volatile Dictionary<string, float> _caps = new(StringComparer.OrdinalIgnoreCase);

    public bool Any => _caps.Count > 0;
    public IReadOnlyCollection<string> Exes => _caps.Keys;

    public void Set(IEnumerable<AppLimit>? limits)
    {
        var caps = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in limits ?? Array.Empty<AppLimit>())
        {
            var exe = Target.NormalizeExe(l?.ExeName ?? "");
            if (exe.Length > 0 && l!.Percent is >= 1 and <= 100) caps.TryAdd(exe, l.Percent / 100f);
        }
        _caps = caps;
    }

    public float? CapOf(string exe) => _caps.TryGetValue(exe, out var c) ? c : null;

    /// <summary>The volume itself, or the app's maximum when it is higher.</summary>
    public float Clamp(string exe, float volume) => CapOf(exe) is { } cap ? Math.Min(volume, cap) : volume;
}

/// <summary>Brings back under their maximum the apps that went above it by themselves. UI thread.</summary>
public sealed class LimitEnforcer
{
    private readonly IAudioSessionService _audio;
    private readonly Func<bool>? _blocked;

    /// <param name="blocked">True while nothing may be written (competitive mode).</param>
    public LimitEnforcer(IAudioSessionService audio, Func<bool>? blocked = null) { _audio = audio; _blocked = blocked; }

    /// <summary>Returns the number of apps that were brought back.</summary>
    public int Enforce()
    {
        if (_blocked?.Invoke() == true) return 0;
        var fixedCount = 0;
        foreach (var exe in _audio.Limits.Exes.ToList())
        {
            if (_audio.Limits.CapOf(exe) is not { } cap) continue;
            if (_audio.GetVolume(exe) is { } v && v > cap + 0.004f && _audio.SetVolume(exe, cap)) fixedCount++;
        }
        return fixedCount;
    }
}
