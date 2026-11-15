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
        return errors;
    }
}
