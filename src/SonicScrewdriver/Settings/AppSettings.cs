using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.Settings;

public sealed class AppSettings
{
    public List<KeyBinding> Bindings { get; set; } = new();
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Switches off what an anti-cheat could flag: the keyboard hook (so all shortcuts), the OSD, the mouse wheel
    /// on the tray icon, and the process lookups behind app icons and friendly names.
    /// </summary>
    public bool CompetitiveMode { get; set; }

    /// <summary>
    /// Key combination that toggles competitive mode, e.g. "Ctrl+Alt+F9"; null or empty means none.
    /// Registered with RegisterHotKey, not the keyboard hook, so it works while competitive mode is on.
    /// </summary>
    public string? CompetitiveHotkey { get; set; }

    /// <summary>
    /// Programs that switch competitive mode on while they run (checked by exe name only, from the process list).
    /// Empty by default, so the feature is off until the user adds one.
    /// </summary>
    public List<AutoProgram> AutoPrograms { get; set; } = new();

    /// <summary>Physical keys that act as media keys (one key per action), for keyboards without them.</summary>
    public List<KeyRemap> KeyRemaps { get; set; } = new();

    /// <summary>Shortcuts on any key combination (not only media keys), registered with RegisterHotKey. Off in competitive mode.</summary>
    public List<FreeHotkey> FreeHotkeys { get; set; } = new();

    /// <summary>Exe names pinned to the top of each device list in the mixer, in pin order.</summary>
    public List<string> PinnedApps { get; set; } = new();

    /// <summary>Exe names not listed in the mixer (they keep working with shortcuts; their volume is not touched).</summary>
    public List<string> HiddenApps { get; set; } = new();

    /// <summary>Output devices included in the cycle (MMDevice IDs); empty means every active device.</summary>
    public List<string> CycleDeviceIds { get; set; } = new();

    /// <summary>
    /// Key combination that mutes every app, then restores them on the second press (per-app mute, the Windows master
    /// is untouched); null or empty means none. Registered with RegisterHotKey; off in competitive mode.
    /// </summary>
    public string? MuteAllHotkey { get; set; }

    /// <summary>Maximum volume per app (1-100 %), applied to the sliders, shortcuts and commands; an app that goes above it is brought back.</summary>
    public List<AppLimit> MaxVolumes { get; set; } = new();

    /// <summary>Named sets of volumes, applied from the tray menu, a shortcut, the command line or when a program starts.</summary>
    public List<VolumeProfile> Profiles { get; set; } = new();

    /// <summary>
    /// Shows what each app plays (title, artist) with Play/Pause, Previous and Next in its mixer row. Off by default; read
    /// only while the mixer is open, and never in competitive mode.
    /// </summary>
    public bool NowPlayingInMixer { get; set; }

    /// <summary>Apps that always play on a given output device, applied when the app's session appears. Off in competitive mode.</summary>
    public List<RoutingRule> RoutingRules { get; set; } = new();

    /// <summary>Choose the default output automatically from <see cref="DevicePriority"/>. Off by default and in competitive mode.</summary>
    public bool DevicePriorityEnabled { get; set; }

    /// <summary>Output devices from most to least wanted (MMDevice IDs): the default output becomes the first one that is plugged in.</summary>
    public List<DeviceRef> DevicePriority { get; set; } = new();

    /// <summary>Key combination that mutes and unmutes the default microphone; null or empty means none. Off in competitive mode.</summary>
    public string? MicMuteHotkey { get; set; }

    /// <summary>Shows the default microphone (volume and mute) at the bottom of the mixer. Off by default and in competitive mode.</summary>
    public bool MicrophoneInMixer { get; set; }

    /// <summary>Automatic ducking: lowers some apps while a voice app speaks. Off by default and in competitive mode.</summary>
    public DuckingSettings Ducking { get; set; } = new();

    /// <summary>Streamer mode: lowers some apps while the user speaks into the microphone. Off by default and in competitive mode.</summary>
    public StreamerSettings Streamer { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
        Bindings = { new KeyBinding(Modifiers.Ctrl, Target.Foreground, 5) },
    };

    /// <summary>Returns user-facing error messages; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var seen = new HashSet<Modifiers>();
        for (var i = 0; i < Bindings.Count; i++)
        {
            var b = Bindings[i];
            var label = $"Row {i + 1}";
            if (b.Modifiers == Modifiers.None)
                errors.Add($"{label}: choose at least one modifier.");
            else if (!seen.Add(b.Modifiers))
                errors.Add($"{label}: this modifier combination is already used.");
            if (b.StepPercent is < 1 or > 50)
                errors.Add($"{label}: step must be between 1 and 50 %.");
            if (b.Target is null || (b.Target.Kind == TargetKind.Process && string.IsNullOrWhiteSpace(b.Target.ExeName)))
                errors.Add($"{label}: enter a target application.");
        }
        var remapKeys = new HashSet<int>();
        var remapActions = new HashSet<MediaKey>();
        foreach (var r in KeyRemaps)
        {
            if (!KeyNames.TryGetVirtualKey(r.Key, out var vk) || !KeyNames.IsRemappable(vk))
                errors.Add($"Media keys: \"{r.Key}\" cannot be used (function keys, Pause, Scroll Lock, Insert, Home, End, Page Up/Down and the numpad only).");
            else if (!remapKeys.Add(vk)) errors.Add($"Media keys: {KeyNames.NameOf(vk)} is assigned to two actions.");
            if (!remapActions.Add(r.Action)) errors.Add($"Media keys: {r.Action} has two keys.");
        }
        var programs = new HashSet<string>();
        for (var i = 0; i < AutoPrograms.Count; i++)
        {
            var exe = Target.NormalizeExe(AutoPrograms[i].ExeName);
            if (exe.Length == 0) errors.Add($"Automatic mode, row {i + 1}: enter a program name, e.g. valorant.exe.");
            else if (!programs.Add(exe)) errors.Add($"Automatic mode, row {i + 1}: {exe} is already in the list.");
        }
        if (!string.IsNullOrWhiteSpace(CompetitiveHotkey) && !Hotkey.TryParse(CompetitiveHotkey, out _))
            errors.Add("Competitive mode shortcut: use at least one modifier plus a key, e.g. Ctrl+Alt+F9.");
        var chords = new HashSet<Hotkey>();
        if (Hotkey.TryParse(CompetitiveHotkey, out var modeChord)) chords.Add(modeChord);
        for (var i = 0; i < FreeHotkeys.Count; i++)
        {
            var f = FreeHotkeys[i];
            var label = $"Free shortcut {i + 1}";
            if (f.Target is null || (f.Target.Kind == TargetKind.Process && string.IsNullOrWhiteSpace(f.Target.ExeName)))
                errors.Add($"{label}: enter a target application.");
            if (f.StepPercent is < 1 or > 50)
                errors.Add($"{label}: step must be between 1 and 50 %.");
            if (!Enum.IsDefined(f.Action)) errors.Add($"{label}: choose an action.");
            if (!f.TryGetHotkey(out var chord))
                errors.Add($"{label}: record a key combination (at least one of Ctrl, Alt, Shift or Win, plus a key).");
            else if (!chords.Add(chord))
                errors.Add($"{label}: {chord} is already used by another shortcut.");
        }
        var limited = new HashSet<string>();
        for (var i = 0; i < MaxVolumes.Count; i++)
        {
            var exe = Target.NormalizeExe(MaxVolumes[i]?.ExeName ?? "");
            var label = $"Maximum volume, row {i + 1}";
            if (exe.Length == 0) errors.Add($"{label}: enter an app name, e.g. chrome.exe.");
            else if (!limited.Add(exe)) errors.Add($"{label}: {exe} is already in the list.");
            if (MaxVolumes[i]?.Percent is null or < 1 or > 100) errors.Add($"{label}: the maximum must be between 1 and 100 %.");
        }
        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Profiles.Count; i++)
        {
            var p = Profiles[i];
            var label = $"Profile {i + 1}";
            if (string.IsNullOrWhiteSpace(p?.Name)) { errors.Add($"{label}: enter a name."); continue; }
            label = $"Profile \"{p.Name.Trim()}\"";
            if (p.Name.Trim().Length > VolumeProfile.MaxNameLength) errors.Add($"{label}: the name is limited to {VolumeProfile.MaxNameLength} characters.");
            if (!profileNames.Add(p.Name.Trim())) errors.Add($"{label}: another profile has the same name.");
            if (p.MasterVolume is < 0 or > 100) errors.Add($"{label}: the master volume must be between 0 and 100 %.");
            var exes = new HashSet<string>();
            foreach (var a in p.Apps ?? new List<ProfileApp>())
            {
                var exe = Target.NormalizeExe(a?.ExeName ?? "");
                if (exe.Length == 0) errors.Add($"{label}: an app row has no name.");
                else if (!exes.Add(exe)) errors.Add($"{label}: {exe} appears twice.");
                if (a is not null && a.Volume is < 0 or > 100) errors.Add($"{label}: the volume of {exe} must be between 0 and 100 %.");
            }
            ValidateChord(p.Hotkey, $"{label} shortcut", chords, errors);
        }
        var routed = new HashSet<string>();
        for (var i = 0; i < RoutingRules.Count; i++)
        {
            var exe = Target.NormalizeExe(RoutingRules[i]?.ExeName ?? "");
            var label = $"Routing, row {i + 1}";
            if (exe.Length == 0) errors.Add($"{label}: enter an app name, e.g. spotify.exe.");
            else if (!routed.Add(exe)) errors.Add($"{label}: {exe} is already in the list.");
            if (string.IsNullOrWhiteSpace(RoutingRules[i]?.DeviceId)) errors.Add($"{label}: choose an output device.");
        }
        if (DevicePriorityEnabled && DevicePriority.Count == 0)
            errors.Add("Output device priority: add at least one device, or turn the list off.");
        if (DevicePriority.Any(d => string.IsNullOrWhiteSpace(d?.Id))) errors.Add("Output device priority: a device has no identifier.");
        if (DevicePriority.Where(d => d is not null).GroupBy(d => d.Id).Any(g => g.Count() > 1)) errors.Add("Output device priority: a device is listed twice.");
        errors.AddRange((Ducking ?? new DuckingSettings()).Validate());
        errors.AddRange((Streamer ?? new StreamerSettings()).Validate());
        ValidateChord(MuteAllHotkey, "Mute everything shortcut", chords, errors);
        ValidateChord(MicMuteHotkey, "Microphone shortcut", chords, errors);
        return errors;
    }

    /// <summary>Empty is fine; otherwise a valid chord that no other shortcut uses.</summary>
    private static void ValidateChord(string? text, string label, HashSet<Hotkey> chords, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Hotkey.TryParse(text, out var chord))
            errors.Add($"{label}: use at least one modifier plus a key, e.g. Ctrl+Alt+M.");
        else if (!chords.Add(chord))
            errors.Add($"{label}: {chord} is already used by another shortcut.");
    }

    /// <summary>The chord in canonical text, or null when it is empty, invalid or already taken (the first one wins).</summary>
    public static string? TakeChord(string? text, HashSet<Hotkey> seen) =>
        Hotkey.TryParse(text, out var chord) && seen.Add(chord) ? chord.ToString() : null;

    /// <summary>"Spotify" / " SPOTIFY.EXE " → "spotify.exe", no empty entries, no duplicates; order kept.</summary>
    public static List<string> SanitizeExeList(IEnumerable<string>? exes)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var e in exes ?? Array.Empty<string>())
        {
            var exe = Target.NormalizeExe(e ?? "");
            if (exe.Length > 0 && seen.Add(exe)) result.Add(exe);
        }
        return result;
    }
}
