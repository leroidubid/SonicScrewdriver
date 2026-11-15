using System.Collections.ObjectModel;
using System.Windows;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<BindingRowViewModel> _rows;

    public event Action<AppSettings>? Saved;

    public List<string> TargetChoices { get; }

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

        var all = current.CycleDeviceIds.Count == 0;
        CycleDevices = audio.GetDevices()
            .Select(d => new CycleDeviceItem(d.Id, d.Name) { Included = all || current.CycleDeviceIds.Contains(d.Id) })
            .ToList();

        DataContext = this;
        RowsList.ItemsSource = _rows;
        StartupBox.IsChecked = current.StartWithWindows;
        CompetitiveBox.IsChecked = current.CompetitiveMode;
        ApplyCompetitiveState();
    }

    private void Competitive_Changed(object sender, RoutedEventArgs e) => ApplyCompetitiveState();

    /// <summary>Everything that depends on the keyboard hook is greyed out and disabled in competitive mode.</summary>
    private void ApplyCompetitiveState()
    {
        var shortcutsOn = CompetitiveBox.IsChecked != true;
        foreach (UIElement element in new UIElement[] { ColumnHeader, AddButton, RowsBorder, CycleSection })
        {
            element.IsEnabled = shortcutsOn;
            element.Opacity = shortcutsOn ? 1 : 0.4;
        }
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

public sealed class CycleDeviceItem
{
    public CycleDeviceItem(string id, string name) { Id = id; Name = name; }
    public string Id { get; }
    public string Name { get; }
    public bool Included { get; set; }
}
