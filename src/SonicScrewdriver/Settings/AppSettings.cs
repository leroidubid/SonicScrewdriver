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

    /// <summary>Output devices included in the cycle (MMDevice IDs); empty means every active device.</summary>
    public List<string> CycleDeviceIds { get; set; } = new();

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
        return errors;
    }
}
