using SonicScrewdriver.Input;

namespace SonicScrewdriver.Bindings;

/// <summary>Human-readable list of configured shortcuts (tray tooltip and menu).</summary>
public static class ShortcutSummary
{
    private const int TooltipMax = 127; // NOTIFYICONDATA.szTip holds 128 chars including the terminator

    public static IReadOnlyList<string> Lines(IEnumerable<KeyBinding> bindings) =>
        bindings.Select(b => $"{Format(b.Modifiers)} → {Format(b.Target)}").ToList();

    /// <summary>"Output: device" line, then one line per shortcut.</summary>
    public static string Tooltip(string? defaultDevice, IReadOnlyList<string> lines)
    {
        var tip = "Output: " + (defaultDevice ?? "none") + "\n" + (lines.Count == 0 ? "No shortcuts" : string.Join("\n", lines));
        return tip.Length <= TooltipMax ? tip : tip[..(TooltipMax - 1)] + "…";
    }

    private static string Format(Modifiers m)
    {
        var parts = new List<string>();
        if (m.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (m.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(Modifiers.Win)) parts.Add("Win");
        return string.Join("+", parts);
    }

    private static string Format(Target t) => t.Kind switch
    {
        TargetKind.Foreground => "Foreground app",
        TargetKind.OutputDevice => "Output devices",
        _ => t.ExeName ?? "",
    };
}
