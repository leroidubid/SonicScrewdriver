using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

/// <summary>The volume profiles card: one editor per profile (name, shortcut, master, program, apps).</summary>
public partial class SettingsWindow
{
    private IAudioSessionService _audioService = null!;
    private readonly List<ProfileEditor> _profileEditors = new();

    private sealed class ProfileEditor
    {
        public Border Root = null!;
        public TextBox Name = null!;
        public KeyCaptureBox Hotkey = null!;
        public CheckBox MasterOn = null!;
        public TextBox Master = null!;
        public ComboBox Start = null!;
        public StackPanel AppsPanel = null!;
        public List<(ComboBox Exe, TextBox Volume)> Apps = new();
        public TextBlock Error = null!;
    }

    private void InitProfiles(AppSettings current)
    {
        foreach (var p in current.Profiles) AddProfileEditor(p);
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e) => AddProfileEditor(null);

    private TextBlock SmallLabel(string text) =>
        new() { Text = text, Foreground = (Brush)FindResource("SubtleBrush"), FontSize = 12, Margin = new Thickness(0, 10, 0, 4) };

    private void AddProfileEditor(VolumeProfile? p)
    {
        var ed = new ProfileEditor();
        ed.Name = new TextBox { Height = 32, Text = p?.Name ?? "", MaxLength = VolumeProfile.MaxNameLength, ToolTip = "Profile name, e.g. Game night" };
        ed.Hotkey = new KeyCaptureBox { Chord = true, Width = 190, Height = 32, Style = (Style)FindResource(typeof(TextBox)), Value = p?.Hotkey };
        ed.Error = new TextBlock { Style = (Style)FindResource("ErrorText") };
        SetupChordBox(ed.Hotkey, ed.Error);
        var clear = new Button { Content = "Clear", Style = (Style)FindResource("FlatButton"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        clear.Click += (_, _) => { ed.Hotkey.Value = null; ed.Error.Text = ""; };
        ed.MasterOn = new CheckBox { Content = "Master volume", IsChecked = p?.MasterVolume is not null, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Also set the volume of the default output device" };
        ed.Master = new TextBox { Width = 48, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, Text = (p?.MasterVolume ?? 50).ToString(), Margin = new Thickness(8, 0, 6, 0) };
        ed.Start = new ComboBox { IsEditable = true, Height = 32, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = AutoChoices, Text = p?.OnProgramStart ?? "", ToolTip = "Optional: apply this profile when this program starts, e.g. game.exe" };
        ed.AppsPanel = new StackPanel();

        var addApp = new Button { Content = "+ Add app", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 0) };
        addApp.Click += (_, _) => AddProfileApp(ed, null);
        var addRunning = new Button { Content = "+ Add running apps", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Add every app that plays sound now, at its current volume" };
        addRunning.Click += (_, _) =>
        {
            foreach (var a in _audioService.GetApps())
                if (!ed.Apps.Any(r => string.Equals(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), a.ExeName, StringComparison.OrdinalIgnoreCase)))
                    AddProfileApp(ed, new ProfileApp(a.ExeName, (int)Math.Round(a.Volume * 100)));
        };
        var remove = new Button { Content = "Remove profile", Style = (Style)FindResource("FlatButton") };
        remove.Click += (_, _) => { ProfilePanel.Children.Remove(ed.Root); _profileEditors.Remove(ed); _chordBoxes.Remove(ed.Hotkey); };

        var chordRow = new StackPanel { Orientation = Orientation.Horizontal };
        chordRow.Children.Add(ed.Hotkey);
        chordRow.Children.Add(clear);
        var masterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        masterRow.Children.Add(ed.MasterOn);
        masterRow.Children.Add(ed.Master);
        masterRow.Children.Add(new TextBlock { Text = "%", Foreground = (Brush)FindResource("SubtleBrush"), VerticalAlignment = VerticalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        buttons.Children.Add(addApp);
        buttons.Children.Add(addRunning);
        buttons.Children.Add(remove);

        var stack = new StackPanel();
        stack.Children.Add(ed.Name);
        var shortcutPart = new StackPanel();
        shortcutPart.Children.Add(SmallLabel("Shortcut (off in competitive mode)"));
        shortcutPart.Children.Add(chordRow);
        shortcutPart.Children.Add(SmallLabel("Apply when this program starts (off in competitive mode)"));
        shortcutPart.Children.Add(ed.Start);
        _shortcutOnly.Add(shortcutPart);
        stack.Children.Add(shortcutPart);
        stack.Children.Add(ed.Error);
        stack.Children.Add(masterRow);
        stack.Children.Add(SmallLabel("Apps (volume 0-100 %)"));
        stack.Children.Add(ed.AppsPanel);
        stack.Children.Add(buttons);
        ed.Root = new Border
        {
            Background = (Brush)FindResource("RowBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = stack,
        };
        foreach (var a in p?.Apps ?? new List<ProfileApp>()) AddProfileApp(ed, a);
        ProfilePanel.Children.Add(ed.Root);
        _profileEditors.Add(ed);
    }

    private void AddProfileApp(ProfileEditor ed, ProfileApp? app)
    {
        var running = _audioService.GetApps().FirstOrDefault(a => string.Equals(a.ExeName, app?.ExeName, StringComparison.OrdinalIgnoreCase));
        var exe = new ComboBox
        {
            IsEditable = true, Height = 32, ItemsSource = AutoChoices, ToolTip = "Executable name, e.g. discord.exe",
            Text = running is null ? app?.ExeName ?? "" : BindingRowViewModel.Label(running.DisplayName, running.ExeName),
        };
        var volume = new TextBox { Width = 48, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, Text = (app?.Volume ?? 50).ToString(), Margin = new Thickness(6, 0, 0, 0) };
        var entry = (exe, volume);
        var row = RemovableRow(ed.AppsPanel, () => ed.Apps.Remove(entry), exe, volume,
            new TextBlock { Text = "%", Foreground = (Brush)FindResource("SubtleBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) });
        row.Background = (Brush)FindResource("BgBrush");
        ed.Apps.Add(entry);
    }

    private List<VolumeProfile> ReadProfiles() => _profileEditors.Select(ed => new VolumeProfile(
        (ed.Name.Text ?? "").Trim(),
        ed.Apps.Select(a => new ProfileApp(BindingRowViewModel.ExeFromLabel(a.Exe.Text ?? ""), int.TryParse(a.Volume.Text, out var v) ? v : -1)).ToList(),
        ed.MasterOn.IsChecked == true ? (int.TryParse(ed.Master.Text, out var m) ? m : -1) : null,
        ed.Hotkey.Value,
        BindingRowViewModel.ExeFromLabel(ed.Start.Text ?? "") is { Length: > 0 } start ? start : null)).ToList();
}
