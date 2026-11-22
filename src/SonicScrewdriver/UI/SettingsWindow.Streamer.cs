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
        ShowNumbers(s.DuckToPercent, s.ThresholdPercent, s.AttackMs, s.ReleaseMs, StreamToBox, StreamThresholdBox, StreamAttackBox, StreamReleaseBox);
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
        var exe = NewExeBox(app?.ExeName, "spotify.exe");
        var lowered = new CheckBox { Content = "Lowered", IsChecked = app?.Lowered ?? true, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lowered while you speak" };
        var row = new StreamRow(exe, lowered);
        RemovableRow(StreamPanel, () => _streamRows.Remove(row), exe, lowered);
        _streamRows.Add(row);
    }

    private StreamerSettings ReadStreamer() => new(
        StreamBox.IsChecked == true,
        Num(StreamToBox), Num(StreamThresholdBox), Num(StreamAttackBox), Num(StreamReleaseBox),
        _streamRows.Select(r => new StreamApp(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), r.Lowered.IsChecked == true)).ToList());
}
