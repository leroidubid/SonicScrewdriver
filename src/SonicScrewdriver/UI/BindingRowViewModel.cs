using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.UI;

/// <summary>Editable row of the settings window.</summary>
public sealed class BindingRowViewModel : ObservableObject
{
    public const string ForegroundLabel = "Foreground app";
    public const string OutputDeviceLabel = "Output device";

    private static readonly System.Text.RegularExpressions.Regex ExeInParens =
        new(@"\(([^()]+\.exe)\)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>"Claude (claude.exe)" in the target list.</summary>
    public static string Label(string displayName, string exe) => $"{displayName} ({exe})";

    /// <summary>"Claude (claude.exe)" -> claude.exe; any other text is returned as typed.</summary>
    public static string ExeFromLabel(string text)
    {
        var match = ExeInParens.Match(text.Trim());
        return match.Success ? match.Groups[1].Value : text.Trim();
    }

    private bool _ctrl, _shift, _alt, _win;
    private string _target = ForegroundLabel;
    private int _step = 5;

    public BindingRowViewModel() { }

    public BindingRowViewModel(KeyBinding b)
    {
        _ctrl = b.Modifiers.HasFlag(Modifiers.Ctrl);
        _shift = b.Modifiers.HasFlag(Modifiers.Shift);
        _alt = b.Modifiers.HasFlag(Modifiers.Alt);
        _win = b.Modifiers.HasFlag(Modifiers.Win);
        _target = b.Target.Kind switch
        {
            TargetKind.Foreground => ForegroundLabel,
            TargetKind.OutputDevice => OutputDeviceLabel,
            _ => b.Target.ExeName ?? "",
        };
        _step = b.StepPercent;
    }

    public bool Ctrl { get => _ctrl; set { _ctrl = value; Raise(); } }
    public bool Shift { get => _shift; set { _shift = value; Raise(); } }
    public bool Alt { get => _alt; set { _alt = value; Raise(); } }
    public bool Win { get => _win; set { _win = value; Raise(); } }
    public string TargetText { get => _target; set { _target = value ?? ""; Raise(); } }
    public int StepPercent { get => _step; set { _step = value; Raise(); } }

    public KeyBinding ToBinding()
    {
        var m = Modifiers.None;
        if (Ctrl) m |= Modifiers.Ctrl;
        if (Shift) m |= Modifiers.Shift;
        if (Alt) m |= Modifiers.Alt;
        if (Win) m |= Modifiers.Win;
        return new KeyBinding(m, ParseTarget(TargetText), StepPercent);
    }

    /// <summary>"Foreground app", "Output device" or an app label / exe name, as typed in a target box.</summary>
    public static Target ParseTarget(string text)
    {
        var t = text.Trim();
        if (string.Equals(t, ForegroundLabel, StringComparison.OrdinalIgnoreCase)) return Target.Foreground;
        if (string.Equals(t, OutputDeviceLabel, StringComparison.OrdinalIgnoreCase)) return Target.OutputDevice;
        return Target.Process(ExeFromLabel(t));
    }

    /// <summary>Text shown in a target box for a target.</summary>
    public static string TargetLabel(Target t) => t.Kind switch
    {
        TargetKind.Foreground => ForegroundLabel,
        TargetKind.OutputDevice => OutputDeviceLabel,
        _ => t.ExeName ?? "",
    };
}
