using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<BindingRowViewModel> _rows;
    private readonly ObservableCollection<AutoProgramRowViewModel> _autoRows;
    private readonly List<(MediaKey Action, KeyCaptureBox Box)> _remapBoxes = new();
    private bool _initialCompetitive; // the checkbox state at the last save (or at opening): "edited" is measured against it
    private readonly Func<Hotkey, bool>? _hotkeyAvailable;
    private readonly List<FreeRow> _freeRows = new();
    private readonly ObservableCollection<string> _hidden;

    /// <summary>Apps the user chose to show again in the mixer (applied on Save).</summary>
    public List<string> ShownAgain { get; } = new();

    public event Action<AppSettings>? Saved;

    /// <summary>True when the user changed the checkbox: otherwise the live state (a shortcut may have toggled it) wins.</summary>
    public bool CompetitiveModeEdited => (CompetitiveBox.IsChecked == true) != _initialCompetitive;

    public List<string> TargetChoices { get; }

    /// <summary>Running apps offered for the automatic list.</summary>
    public List<string> AutoChoices { get; }

    /// <summary>Active output devices with a checkbox: included in the Output device cycle.</summary>
    public List<CycleDeviceItem> CycleDevices { get; }

    public SettingsWindow(AppSettings current, IAudioSessionService audio, Func<Hotkey, bool>? hotkeyAvailable = null)
    {
        InitializeComponent();
        _hotkeyAvailable = hotkeyAvailable;
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
        foreach (var f in current.FreeHotkeys) AddFreeRow(f);
        ModeHotkeyBox.Validator = chord => FreeChordConflict(chord, null);
        InitExtras(current, audio);
        _hidden = new ObservableCollection<string>(current.HiddenApps);
        _hidden.CollectionChanged += (_, _) => HiddenEmpty.Visibility = _hidden.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenList.ItemsSource = _hidden;
        HiddenEmpty.Visibility = _hidden.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DataContext = this;
        RowsList.ItemsSource = _rows;
        StartupBox.IsChecked = current.StartWithWindows;
        CompetitiveBox.IsChecked = _initialCompetitive = current.CompetitiveMode;
        ModeHotkeyBox.Value = current.CompetitiveHotkey;
        ModeHotkeyBox.Rejected += message => HotkeyError.Text = message;
        ApplyCompetitiveState();
        // Any edit marks the window as modified (Close becomes Cancel, the "Saved" note goes away). Text and selection
        // changes only count while the keyboard focus is in the control: bindings that fill a tab when it is first shown do not.
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (e.OriginalSource is UIElement { IsKeyboardFocusWithin: true }) MarkDirty(); }));
        AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is UIElement { IsKeyboardFocusWithin: true } and not TabControl) MarkDirty(); }));
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is Button b && (b == SaveButton || b == SaveExitButton || b == CancelButton)) return;
            MarkDirty();
        }));
        _savedTimer.Tick += (_, _) => { _savedTimer.Stop(); SavedText.Visibility = Visibility.Collapsed; };
        Closed += (_, _) => _savedTimer.Stop();
    }

    private readonly System.Windows.Threading.DispatcherTimer _savedTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    private void MarkDirty()
    {
        CancelButton.Content = "Cancel";
        _savedTimer.Stop();
        SavedText.Visibility = Visibility.Collapsed;
    }

    /// <summary>After a save the window behaves as unmodified again: Cancel is Close, and "Saved" shows for a moment.</summary>
    private void MarkSaved()
    {
        CancelButton.Content = "Close";
        SavedText.Visibility = Visibility.Visible;
        _savedTimer.Stop();
        _savedTimer.Start();
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
        if (_dimmed is null) BuildCompetitiveMarks();
        foreach (var part in _dimmed!) { part.IsEnabled = shortcutsOn; part.Opacity = shortcutsOn ? 1 : 0.4; }
        foreach (var badge in _badges.Concat(_tabMarks)) badge.Visibility = shortcutsOn ? Visibility.Collapsed : Visibility.Visible;
    }

    private List<FrameworkElement>? _dimmed;                       // what is greyed out in competitive mode
    private readonly List<FrameworkElement> _badges = new();       // one "Competitive mode" label per card with greyed options
    private readonly List<FrameworkElement> _tabMarks = new();     // the same label on the header of each tab that has such a card

    /// <summary>
    /// Finds the cards that hold greyed-out options and gives each a "Competitive mode" label (not greyed itself).
    /// The shortcut cards are greyed as a whole (their content, so the label stays readable); the other cards only
    /// have their listed parts greyed.
    /// </summary>
    private void BuildCompetitiveMarks()
    {
        _dimmed = new List<FrameworkElement>(_shortcutOnly);
        var badgedCards = new List<Border>();
        var cardStyle = (Style)FindResource("Card");
        foreach (var card in LogicalDescendants(this).OfType<Border>().Where(b => b.Style == cardStyle).ToList())
        {
            var wholeCard = Inside(card, ShortcutsSection) || Equals(card.Tag, "Dim");
            if (!wholeCard && !_shortcutOnly.Any(part => Inside(part, card))) continue;
            var content = (FrameworkElement)card.Child;
            card.Child = null;
            var badge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 9, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("ErrorBrush"),
                Visibility = Visibility.Collapsed,
                ToolTip = "Competitive mode is on: the greyed options are switched off.",
                Child = new TextBlock { Text = "Competitive mode", FontSize = 11, Foreground = (Brush)FindResource("ErrorBrush") },
            };
            var grid = new Grid();
            grid.Children.Add(content);
            grid.Children.Add(badge);
            card.Child = grid;
            _badges.Add(badge);
            badgedCards.Add(card);
            if (wholeCard) _dimmed.Add(content);
        }
        // Tab headers: the title, plus a small red label on tabs that hold greyed options. The logical tree is walked
        // (tabs that were never shown are not in the visual tree).
        foreach (var tab in Tabs.Items.OfType<TabItem>())
        {
            var title = new TextBlock { Text = tab.Header as string ?? "", VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeights.SemiBold };
            title.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = tab });
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(title);
            if (badgedCards.Any(c => Inside(c, tab)))
            {
                var mark = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 1, 0, 0),
                    BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("ErrorBrush"), Visibility = Visibility.Collapsed,
                    ToolTip = "Competitive mode is on: some options of this tab are switched off.",
                    Child = new TextBlock { Text = "Competitive mode", FontSize = 10, FontWeight = FontWeights.Normal, Foreground = (Brush)FindResource("ErrorBrush") },
                };
                header.Children.Add(mark);
                _tabMarks.Add(mark);
            }
            tab.Header = header;
        }
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in LogicalDescendants(child)) yield return d;
        }
    }

    private static bool Inside(DependencyObject element, DependencyObject ancestor)
    {
        for (var e = element; e is not null; e = LogicalTreeHelper.GetParent(e) ?? (e as FrameworkElement)?.Parent ?? VisualTreeHelper.GetParent(e))
            if (ReferenceEquals(e, ancestor) && !ReferenceEquals(element, ancestor)) return true;
        return false;
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

    private sealed record FreeRow(KeyCaptureBox Box, ComboBox Action, ComboBox Target, TextBox Step);

    private void AddFree_Click(object sender, RoutedEventArgs e) => AddFreeRow(null);

    /// <summary>One row per free shortcut: chord box, action, target, step and Remove.</summary>
    private void AddFreeRow(FreeHotkey? f)
    {
        var box = new KeyCaptureBox { Chord = true, Width = 170, Height = 32, Style = (Style)FindResource(typeof(TextBox)), Value = f?.Chord };
        var action = new ComboBox
        {
            Width = 120, Height = 32, Margin = new Thickness(6, 0, 0, 0),
            ItemsSource = RemapActions.Select(a => a.Label).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(RemapActions, a => a.Action == (f?.Action ?? MediaKey.VolumeUp))),
        };
        var target = new ComboBox
        {
            IsEditable = true, Height = 32, MinWidth = 150, Margin = new Thickness(6, 0, 6, 0), ItemsSource = TargetChoices,
            Text = f is null ? BindingRowViewModel.ForegroundLabel : BindingRowViewModel.TargetLabel(f.Target),
            ToolTip = "\"Foreground app\", \"Output device\" or an executable name, e.g. spotify.exe",
        };
        var step = new TextBox { Width = 44, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, Text = (f?.StepPercent ?? 5).ToString(), ToolTip = "Volume step per press (%)" };
        var remove = new Button
        {
            Content = "\uE74D", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), ToolTip = "Remove",
            Width = 32, Height = 32, Padding = new Thickness(0), Style = (Style)FindResource("FlatButton"),
        };
        var grid = new Grid();
        for (var i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var parts = new FrameworkElement[] { box, action, target, step, remove };
        for (var i = 0; i < parts.Length; i++) { Grid.SetColumn(parts[i], i); grid.Children.Add(parts[i]); }
        var border = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("RowBrush"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = grid,
        };
        var row = new FreeRow(box, action, target, step);
        box.Validator = chord => FreeChordConflict(chord, row);
        box.Rejected += message => FreeError.Text = message;
        remove.Click += (_, _) => { FreePanel.Children.Remove(border); _freeRows.Remove(row); FreeError.Text = ""; };
        FreePanel.Children.Add(border);
        _freeRows.Add(row);
    }

    /// <summary>Error for a chord already used in this window or refused by Windows; null when it can be used.</summary>
    private string? FreeChordConflict(string chord, FreeRow? self)
    {
        if (_freeRows.Any(r => r != self && string.Equals(r.Box.Value, chord, StringComparison.OrdinalIgnoreCase)))
            return $"{chord} is already used by another free shortcut. Clear it first.";
        if (_chordBoxes.Any(b => string.Equals(b.Value, chord, StringComparison.OrdinalIgnoreCase)))
            return $"{chord} is already used by another shortcut. Clear it first.";
        if (self is not null && string.Equals(ModeHotkeyBox.Value, chord, StringComparison.OrdinalIgnoreCase))
            return $"{chord} is the competitive mode shortcut. Choose another combination.";
        if (self is not null && Hotkey.TryParse(chord, out var h) && _hotkeyAvailable?.Invoke(h) == false)
            return $"{chord} is already used by Windows or another program. Choose another combination.";
        return null;
    }

    private void ShowAgain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string exe }) return;
        _hidden.Remove(exe);
        ShownAgain.Add(exe);
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

    /// <summary>Saves and applies, keeping the window open (and unmodified).</summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Commit()) MarkSaved();
    }

    /// <summary>Saves, applies and closes.</summary>
    private void SaveExit_Click(object sender, RoutedEventArgs e)
    {
        if (Commit()) Close();
    }

    /// <summary>Reads the window, validates it and raises <see cref="Saved"/>. False (window stays open) when there are errors.</summary>
    private bool Commit()
    {
        var settings = new AppSettings
        {
            Bindings = _rows.Select(r => r.ToBinding()).ToList(),
            StartWithWindows = StartupBox.IsChecked == true,
            CompetitiveMode = CompetitiveBox.IsChecked == true,
            CompetitiveHotkey = ModeHotkeyBox.Value,
            FreeHotkeys = _freeRows.Select(r => new FreeHotkey(r.Box.Value ?? "", RemapActions[Math.Max(0, r.Action.SelectedIndex)].Action,
                BindingRowViewModel.ParseTarget(r.Target.Text ?? ""), int.TryParse(r.Step.Text, out var step) ? step : 0)).ToList(),
            KeyRemaps = _remapBoxes.Where(r => r.Box.Value is not null).Select(r => new KeyRemap(r.Action, r.Box.Value!)).ToList(),
            AutoPrograms = _autoRows.Select(r => new AutoProgram(BindingRowViewModel.ExeFromLabel(r.ExeText), r.Enabled)).ToList(),
            // All checked is stored as "all" (empty), so devices plugged in later join the cycle.
            CycleDeviceIds = CycleDevices.All(d => d.Included)
                ? new List<string>()
                : CycleDevices.Where(d => d.Included).Select(d => d.Id).ToList(),
        };
        ReadExtras(settings);
        var errors = settings.Validate().ToList();
        errors.AddRange(ExtraErrors());
        if (CycleDevices.Count > 0 && CycleDevices.All(d => !d.Included))
            errors.Add("Select at least one output device to cycle through.");
        ErrorList.ItemsSource = errors;
        if (errors.Count > 0) return false;
        Saved?.Invoke(settings);
        // Later saves compare with this state: the live competitive mode wins again unless the box is changed once more,
        // and an app shown again is not shown again (nor re-shown after being hidden anew from the mixer) by the next save.
        _initialCompetitive = settings.CompetitiveMode;
        ShownAgain.Clear();
        return true;
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
