using System.Windows;
using System.Windows.Controls;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

/// <summary>The cards added after the first release (mute everything, profiles, limits, routing, ...): their controls and their read-back.</summary>
public partial class SettingsWindow
{
    /// <summary>Single-chord boxes other than the competitive one: a chord is unique across all of them and the free shortcuts.</summary>
    private readonly List<KeyCaptureBox> _chordBoxes = new();
    /// <summary>Parts of cards outside the Shortcuts section that depend on shortcuts or observers: greyed out in competitive mode.</summary>
    private readonly List<FrameworkElement> _shortcutOnly = new();

    private void InitExtras(AppSettings current, IAudioSessionService audio)
    {
        _audioService = audio;
        _deviceChoices = audio.GetDevices().Select(d => new DeviceChoice(d.Id, d.Name)).ToList();
        MuteAllBox.Value = current.MuteAllHotkey;
        SetupChordBox(MuteAllBox, MuteAllError);
        MicBox.Value = current.MicMuteHotkey;
        MicMixerBox.IsChecked = current.MicrophoneInMixer;
        SetupChordBox(MicBox, MicError);
        foreach (var l in current.MaxVolumes) AddLimitRow(l);
        InitProfiles(current);
        InitDucking(current);
        InitStreamer(current);
        InitPriority(current);
        InitRouting(current);
        NowPlayingBox.IsChecked = current.NowPlayingInMixer;
        _shortcutOnly.Add(NowPlayingBox);
        _shortcutOnly.Add(NowPlayingHint);
        ApplyCompetitiveState();
    }

    private void ReadExtras(AppSettings s)
    {
        s.MuteAllHotkey = MuteAllBox.Value;
        s.MicMuteHotkey = MicBox.Value;
        s.MicrophoneInMixer = MicMixerBox.IsChecked == true;
        s.Profiles = ReadProfiles();
        s.Ducking = ReadDucking();
        s.Streamer = ReadStreamer();
        ReadPriority(s);
        s.RoutingRules = ReadRouting();
        s.NowPlayingInMixer = NowPlayingBox.IsChecked == true;
        s.MaxVolumes = _limitRows.Select(r => new AppLimit(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), int.TryParse(r.Percent.Text, out var p) ? p : 0)).ToList();
    }

    private IEnumerable<string> ExtraErrors() => Enumerable.Empty<string>();

    private sealed record LimitRow(ComboBox Exe, TextBox Percent);
    private readonly List<LimitRow> _limitRows = new();

    private void AddLimit_Click(object sender, RoutedEventArgs e) => AddLimitRow(null);

    private void AddLimitRow(AppLimit? limit)
    {
        var exe = new ComboBox { IsEditable = true, Height = 32, ItemsSource = AutoChoices, Text = limit?.ExeName ?? "", ToolTip = "Executable name, e.g. chrome.exe" };
        var percent = new TextBox { Width = 48, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, Text = (limit?.Percent ?? 50).ToString(), ToolTip = "Maximum volume (1-100 %)", Margin = new Thickness(6, 0, 0, 0) };
        var row = new LimitRow(exe, percent);
        var border = RemovableRow(LimitPanel, () => _limitRows.Remove(row), exe, percent, new TextBlock { Text = "%", Foreground = (System.Windows.Media.Brush)FindResource("SubtleBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) });
        _limitRows.Add(row);
    }

    /// <summary>A card row: the first part stretches, then the others, then a Remove button.</summary>
    private Border RemovableRow(Panel host, Action onRemove, params FrameworkElement[] parts)
    {
        var grid = new Grid();
        for (var i = 0; i <= parts.Length; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        for (var i = 0; i < parts.Length; i++) { Grid.SetColumn(parts[i], i); grid.Children.Add(parts[i]); }
        var remove = new Button
        {
            Content = "", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), ToolTip = "Remove",
            Width = 32, Height = 32, Padding = new Thickness(0), Style = (Style)FindResource("FlatButton"),
        };
        Grid.SetColumn(remove, parts.Length);
        grid.Children.Add(remove);
        var border = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("RowBrush"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = grid,
        };
        remove.Click += (_, _) => { host.Children.Remove(border); onRemove(); };
        host.Children.Add(border);
        return border;
    }

    /// <summary>A chord box that refuses chords used elsewhere in the window or taken by another program.</summary>
    private void SetupChordBox(KeyCaptureBox box, TextBlock error)
    {
        _chordBoxes.Add(box);
        box.Validator = chord =>
        {
            if (string.Equals(ModeHotkeyBox.Value, chord, StringComparison.OrdinalIgnoreCase))
                return $"{chord} is the competitive mode shortcut. Choose another combination.";
            if (_freeRows.Any(r => string.Equals(r.Box.Value, chord, StringComparison.OrdinalIgnoreCase)))
                return $"{chord} is already used by a free shortcut. Choose another combination.";
            if (_chordBoxes.Any(b => b != box && string.Equals(b.Value, chord, StringComparison.OrdinalIgnoreCase)))
                return $"{chord} is already used by another shortcut. Choose another combination.";
            if (Input.Hotkey.TryParse(chord, out var h) && _hotkeyAvailable?.Invoke(h) == false)
                return $"{chord} is already used by Windows or another program. Choose another combination.";
            return null;
        };
        box.Rejected += message => error.Text = message;
    }

    private void ClearMic_Click(object sender, RoutedEventArgs e)
    {
        MicBox.Value = null;
        MicError.Text = "";
    }

    private void ClearMuteAll_Click(object sender, RoutedEventArgs e)
    {
        MuteAllBox.Value = null;
        MuteAllError.Text = "";
    }
}
