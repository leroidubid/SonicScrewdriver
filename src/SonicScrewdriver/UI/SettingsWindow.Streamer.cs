using System.Windows;
using System.Windows.Controls;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

/// <summary>The streamer mode card: the numbers, and a list of apps with a "Lowered" tick.</summary>
public partial class SettingsWindow
{
    private sealed record StreamRow(ComboBox Exe, CheckBox Lowered);
    private readonly List<StreamRow> _streamRows = new();

    private void InitStreamer(AppSettings current)
    {
        var s = current.Streamer;
        StreamBox.IsChecked = s.Enabled;
        StreamToBox.Text = s.DuckToPercent.ToString();
        StreamThresholdBox.Text = s.ThresholdPercent.ToString();
        StreamAttackBox.Text = s.AttackMs.ToString();
        StreamReleaseBox.Text = s.ReleaseMs.ToString();
        foreach (var a in s.AppList) AddStreamRow(a);
        // The mode reads the microphone level: the whole card is greyed out in competitive mode.
        _shortcutOnly.Add(StreamBox);
        _shortcutOnly.Add(StreamHint);
        _shortcutOnly.Add(StreamSettingsPanel);
    }

    private void AddStream_Click(object sender, RoutedEventArgs e) => AddStreamRow(null);

    private void AddStreamRunning_Click(object sender, RoutedEventArgs e)
    {
        var present = _streamRows.Select(r => BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? "")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in _audioService.GetApps())
            if (present.Add(app.ExeName)) AddStreamRow(new StreamApp(app.ExeName, true));
    }

    private void AddStreamRow(StreamApp? app)
    {
        var running = _audioService.GetApps().FirstOrDefault(a => string.Equals(a.ExeName, app?.ExeName, StringComparison.OrdinalIgnoreCase));
        var exe = new ComboBox
        {
            IsEditable = true, Height = 32, MinWidth = 150, ItemsSource = AutoChoices, ToolTip = "Executable name, e.g. spotify.exe",
            Text = running is null ? app?.ExeName ?? "" : BindingRowViewModel.Label(running.DisplayName, running.ExeName),
        };
        var lowered = new CheckBox { Content = "Lowered", IsChecked = app?.Lowered ?? true, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lowered while you speak" };
        var row = new StreamRow(exe, lowered);
        RemovableRow(StreamPanel, () => _streamRows.Remove(row), exe, lowered);
        _streamRows.Add(row);
    }

    private StreamerSettings ReadStreamer() => new(
        StreamBox.IsChecked == true,
        int.TryParse(StreamToBox.Text, out var to) ? to : -1,
        int.TryParse(StreamThresholdBox.Text, out var th) ? th : -1,
        int.TryParse(StreamAttackBox.Text, out var at) ? at : -1,
        int.TryParse(StreamReleaseBox.Text, out var rel) ? rel : -1,
        _streamRows.Select(r => new StreamApp(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), r.Lowered.IsChecked == true)).ToList());
}
