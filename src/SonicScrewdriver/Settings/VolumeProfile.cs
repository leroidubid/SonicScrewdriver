using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.Settings;

/// <summary>An app's volume (0-100 %) inside a profile.</summary>
public sealed record ProfileApp(string ExeName, int Volume);

/// <summary>
/// A named set of volumes ("Game night": Discord 80, music 30, browser 10), applied from the tray menu, a shortcut,
/// the command line or when a program starts. <see cref="MasterVolume"/> (0-100) is optional.
/// </summary>
public sealed record VolumeProfile(string Name, List<ProfileApp> Apps, int? MasterVolume = null, string? Hotkey = null, string? OnProgramStart = null)
{
    public const int MaxNameLength = 40;

    /// <summary>
    /// Makes hand-edited profiles safe: trimmed names (no empty, no duplicate, the first wins), exe names normalized
    /// and unique per profile, volumes clamped to 0-100, chords canonical and unique (<paramref name="chords"/> holds the
    /// ones already taken and receives the new ones).
    /// </summary>
    public static List<VolumeProfile> Sanitize(IEnumerable<VolumeProfile?>? profiles, HashSet<Hotkey> chords)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<VolumeProfile>();
        foreach (var p in profiles ?? Array.Empty<VolumeProfile?>())
        {
            var name = (p?.Name ?? "").Trim();
            if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd();
            if (p is null || name.Length == 0 || !names.Add(name)) continue;
            var exes = new HashSet<string>();
            var apps = new List<ProfileApp>();
            foreach (var a in p.Apps ?? new List<ProfileApp>())
            {
                var exe = Target.NormalizeExe(a?.ExeName ?? "");
                if (exe.Length > 0 && exes.Add(exe)) apps.Add(new ProfileApp(exe, Math.Clamp(a!.Volume, 0, 100)));
            }
            var start = Target.NormalizeExe(p.OnProgramStart ?? "");
            result.Add(new VolumeProfile(name, apps, p.MasterVolume is { } m ? Math.Clamp(m, 0, 100) : null,
                AppSettings.TakeChord(p.Hotkey, chords), start.Length == 0 ? null : start));
        }
        return result;
    }

    /// <summary>The profile with this name (case-insensitive), or null.</summary>
    public static VolumeProfile? Find(IEnumerable<VolumeProfile> profiles, string name) =>
        profiles.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
