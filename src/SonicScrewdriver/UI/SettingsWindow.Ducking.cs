using System.Windows;
using System.Windows.Controls;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

/// <summary>The automatic ducking card: the numbers, and a list of apps with Voice / Ducked ticks.</summary>
public partial class SettingsWindow
{
    private sealed record DuckRow(ComboBox Exe, CheckBox Voice, CheckBox Ducked);
    private readonly List<DuckRow> _duckRows = new();

    private void InitDucking(AppSettings current)
    {
        var d = current.Ducking;
        DuckBox.IsChecked = d.Enabled;
        ShowNumbers(d.DuckToPercent, d.ThresholdPercent, d.AttackMs, d.ReleaseMs, DuckToBox, DuckThresholdBox, DuckAttackBox, DuckReleaseBox);
        foreach (var a in d.AppList) AddDuckRow(a);
        // Ducking watches sound levels: the whole card is greyed out in competitive mode.
        _shortcutOnly.Add(DuckBox);
        _shortcutOnly.Add(DuckHint);
        _shortcutOnly.Add(DuckSettingsPanel);
    }

    private void AddDuck_Click(object sender, RoutedEventArgs e) => AddDuckRow(null);

    private void AddDuckRow(DuckApp? app)
    {
        var exe = NewExeBox(app?.ExeName, "discord.exe");
        var voice = new CheckBox { Content = "Voice", IsChecked = app?.Voice ?? false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Its speech lowers the other apps" };
        var ducked = new CheckBox { Content = "Lowered", IsChecked = app?.Ducked ?? true, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lowered while a voice app speaks" };
        var row = new DuckRow(exe, voice, ducked);
        RemovableRow(DuckPanel, () => _duckRows.Remove(row), exe, voice, ducked);
        _duckRows.Add(row);
    }

    // Shared with the streamer card: the same four numbers, and an app name box that shows the running app's label.
    private static void ShowNumbers(int to, int threshold, int attack, int release, TextBox toBox, TextBox thresholdBox, TextBox attackBox, TextBox releaseBox)
    {
        toBox.Text = to.ToString();
        thresholdBox.Text = threshold.ToString();
        attackBox.Text = attack.ToString();
        releaseBox.Text = release.ToString();
    }

    private static int Num(TextBox box) => int.TryParse(box.Text, out var v) ? v : -1; // -1: rejected by validation

    private ComboBox NewExeBox(string? exeName, string example)
    {
        var running = _audioService.GetApps().FirstOrDefault(a => string.Equals(a.ExeName, exeName, StringComparison.OrdinalIgnoreCase));
        return new ComboBox
        {
            IsEditable = true, Height = 32, MinWidth = 150, ItemsSource = AutoChoices, ToolTip = "Executable name, e.g. " + example,
            Text = running is null ? exeName ?? "" : BindingRowViewModel.Label(running.DisplayName, running.ExeName),
        };
    }

    private DuckingSettings ReadDucking() => new(
        DuckBox.IsChecked == true,
        Num(DuckToBox), Num(DuckThresholdBox), Num(DuckAttackBox), Num(DuckReleaseBox),
        _duckRows.Select(r => new DuckApp(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), r.Voice.IsChecked == true, r.Ducked.IsChecked == true)).ToList());
}
