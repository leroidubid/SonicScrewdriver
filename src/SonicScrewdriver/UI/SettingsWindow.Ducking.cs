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
        DuckToBox.Text = d.DuckToPercent.ToString();
        DuckThresholdBox.Text = d.ThresholdPercent.ToString();
        DuckAttackBox.Text = d.AttackMs.ToString();
        DuckReleaseBox.Text = d.ReleaseMs.ToString();
        foreach (var a in d.AppList) AddDuckRow(a);
        // Ducking watches sound levels: the whole card is greyed out in competitive mode.
        _shortcutOnly.Add(DuckBox);
        _shortcutOnly.Add(DuckHint);
        _shortcutOnly.Add(DuckSettingsPanel);
    }

    private void AddDuck_Click(object sender, RoutedEventArgs e) => AddDuckRow(null);

    private void AddDuckRow(DuckApp? app)
    {
        var running = _audioService.GetApps().FirstOrDefault(a => string.Equals(a.ExeName, app?.ExeName, StringComparison.OrdinalIgnoreCase));
        var exe = new ComboBox
        {
            IsEditable = true, Height = 32, MinWidth = 150, ItemsSource = AutoChoices, ToolTip = "Executable name, e.g. discord.exe",
            Text = running is null ? app?.ExeName ?? "" : BindingRowViewModel.Label(running.DisplayName, running.ExeName),
        };
        var voice = new CheckBox { Content = "Voice", IsChecked = app?.Voice ?? false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Its speech lowers the other apps" };
        var ducked = new CheckBox { Content = "Lowered", IsChecked = app?.Ducked ?? true, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lowered while a voice app speaks" };
        var row = new DuckRow(exe, voice, ducked);
        RemovableRow(DuckPanel, () => _duckRows.Remove(row), exe, voice, ducked);
        _duckRows.Add(row);
    }

    private DuckingSettings ReadDucking() => new(
        DuckBox.IsChecked == true,
        int.TryParse(DuckToBox.Text, out var to) ? to : -1,
        int.TryParse(DuckThresholdBox.Text, out var th) ? th : -1,
        int.TryParse(DuckAttackBox.Text, out var at) ? at : -1,
        int.TryParse(DuckReleaseBox.Text, out var rel) ? rel : -1,
        _duckRows.Select(r => new DuckApp(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), r.Voice.IsChecked == true, r.Ducked.IsChecked == true)).ToList());
}
