using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<BindingRowViewModel> _rows;
    private readonly ObservableCollection<AutoProgramRowViewModel> _autoRows;
    private readonly List<(MediaKey Action, KeyCaptureBox Box)> _remapBoxes = new();
    private readonly bool _initialCompetitive;

    public event Action<AppSettings>? Saved;

    /// <summary>True when the user changed the checkbox: otherwise the live state (a shortcut may have toggled it) wins.</summary>
    public bool CompetitiveModeEdited => (CompetitiveBox.IsChecked == true) != _initialCompetitive;

    public List<string> TargetChoices { get; }

    /// <summary>Running apps offered for the automatic list.</summary>
    public List<string> AutoChoices { get; }

    /// <summary>Active output devices with a checkbox: included in the Output device cycle.</summary>
    public List<CycleDeviceItem> CycleDevices { get; }

    public SettingsWindow(AppSettings current, IAudioSessionService audio)
    {
        InitializeComponent();
        _rows = new ObservableCollection<BindingRowViewModel>(current.Bindings.Select(b => new BindingRowViewModel(b)));

        // Running apps as "Claude (claude.exe)"; configured exes not running stay as plain exe names.
        var running = audio.GetApps().Select(a => BindingRowViewModel.Label(a.DisplayName, a.ExeName)).ToList();
        var configured = current.Bindings
            .Where(b => b.Target.ExeName is not null && !audio.GetApps().Any(a => a.ExeName == b.Target.ExeName))
            .Select(b => b.Target.ExeName!);
        TargetChoices = new[] { BindingRowViewModel.ForegroundLabel, BindingRowViewModel.OutputDeviceLabel }
            .Concat(running).Concat(configured)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Show configured rows with the same "Name (exe)" label when the app is running.
        foreach (var row in _rows)
        {
            var app = audio.GetApps().FirstOrDefault(a => string.Equals(a.ExeName, row.TargetText, StringComparison.OrdinalIgnoreCase));
            if (app is not null) row.TargetText = BindingRowViewModel.Label(app.DisplayName, app.ExeName);
        }

        AutoChoices = running.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _autoRows = new ObservableCollection<AutoProgramRowViewModel>(
            current.AutoPrograms.Select(p => new AutoProgramRowViewModel(p.ExeName, p.Enabled)));
        AutoList.ItemsSource = _autoRows;

        var all = current.CycleDeviceIds.Count == 0;
        CycleDevices = audio.GetDevices()
            .Select(d => new CycleDeviceItem(d.Id, d.Name) { Included = all || current.CycleDeviceIds.Contains(d.Id) })
            .ToList();

        BuildRemapRows(current);
        DataContext = this;
        RowsList.ItemsSource = _rows;
        StartupBox.IsChecked = current.StartWithWindows;
        CompetitiveBox.IsChecked = _initialCompetitive = current.CompetitiveMode;
        ModeHotkeyBox.Value = current.CompetitiveHotkey;
        ModeHotkeyBox.Rejected += message => HotkeyError.Text = message;
        ApplyCompetitiveState();
    }

    /// <summary>Dark title bar to match the window (Windows 10 build 18985 and later, and Windows 11; ignored otherwise).</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        }
        catch (Exception) { /* cosmetic only */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void Competitive_Changed(object sender, RoutedEventArgs e) => ApplyCompetitiveState();

    /// <summary>Everything that depends on the keyboard hook is greyed out and disabled in competitive mode.</summary>
    private void ApplyCompetitiveState()
    {
        var shortcutsOn = CompetitiveBox.IsChecked != true;
        ShortcutsSection.IsEnabled = shortcutsOn;
        ShortcutsSection.Opacity = shortcutsOn ? 1 : 0.4;
    }

    private static readonly (MediaKey Action, string Label)[] RemapActions =
    {
        (MediaKey.VolumeUp, "Volume up"), (MediaKey.VolumeDown, "Volume down"), (MediaKey.Mute, "Mute"),
        (MediaKey.PlayPause, "Play / Pause"), (MediaKey.Next, "Next track"), (MediaKey.Previous, "Previous track"),
    };

    /// <summary>One row per media key: its label, a box that records the next key press, and Clear.</summary>
    private void BuildRemapRows(AppSettings current)
    {
        foreach (var (action, label) in RemapActions)
        {
            var box = new KeyCaptureBox { Width = 180, Height = 32, Style = (Style)FindResource(typeof(TextBox)), Value = current.KeyRemaps.FirstOrDefault(r => r.Action == action)?.Key };
            box.Validator = key =>
            {
                var other = _remapBoxes.FirstOrDefault(r => r.Action != action && string.Equals(r.Box.Value, key, StringComparison.OrdinalIgnoreCase));
                return other.Box is null ? null : $"{key} is already used for {RemapActions.First(a => a.Action == other.Action).Label}. Clear it first.";
            };
            box.Rejected += message => RemapError.Text = message;
            var clear = new Button { Content = "Clear", Style = (Style)FindResource("FlatButton"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
            clear.Click += (_, _) => { box.Value = null; RemapError.Text = ""; };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            row.Children.Add(clear);
            RemapPanel.Children.Add(row);
            _remapBoxes.Add((action, box));
        }
    }

    private void ClearModeHotkey_Click(object sender, RoutedEventArgs e)
    {
        ModeHotkeyBox.Value = null;
        HotkeyError.Text = "";
    }

    private void AddAuto_Click(object sender, RoutedEventArgs e) => _autoRows.Add(new AutoProgramRowViewModel("", true));

    private void RemoveAuto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AutoProgramRowViewModel row }) _autoRows.Remove(row);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _rows.Add(new BindingRowViewModel());

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BindingRowViewModel row }) _rows.Remove(row);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            Bindings = _rows.Select(r => r.ToBinding()).ToList(),
            StartWithWindows = StartupBox.IsChecked == true,
            CompetitiveMode = CompetitiveBox.IsChecked == true,
            CompetitiveHotkey = ModeHotkeyBox.Value,
            KeyRemaps = _remapBoxes.Where(r => r.Box.Value is not null).Select(r => new KeyRemap(r.Action, r.Box.Value!)).ToList(),
            AutoPrograms = _autoRows.Select(r => new AutoProgram(BindingRowViewModel.ExeFromLabel(r.ExeText), r.Enabled)).ToList(),
            // All checked is stored as "all" (empty), so devices plugged in later join the cycle.
            CycleDeviceIds = CycleDevices.All(d => d.Included)
                ? new List<string>()
                : CycleDevices.Where(d => d.Included).Select(d => d.Id).ToList(),
        };
        var errors = settings.Validate().ToList();
        if (CycleDevices.Count > 0 && CycleDevices.All(d => !d.Included))
            errors.Add("Select at least one output device to cycle through.");
        ErrorList.ItemsSource = errors;
        if (errors.Count > 0) return;
        Saved?.Invoke(settings);
        Close();
    }
}

/// <summary>Editable row of the automatic competitive-mode list.</summary>
public sealed class AutoProgramRowViewModel : ObservableObject
{
    private bool _enabled;
    private string _exe;

    public AutoProgramRowViewModel(string exe, bool enabled) { _exe = exe; _enabled = enabled; }

    public bool Enabled { get => _enabled; set { _enabled = value; Raise(); } }
    public string ExeText { get => _exe; set { _exe = value ?? ""; Raise(); } }
}

public sealed class CycleDeviceItem
{
    public CycleDeviceItem(string id, string name) { Id = id; Name = name; }
    public string Id { get; }
    public string Name { get; }
    public bool Included { get; set; }
}
