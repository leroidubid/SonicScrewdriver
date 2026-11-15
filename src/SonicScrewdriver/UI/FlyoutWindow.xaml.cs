using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Interop;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

public partial class FlyoutWindow : Window
{
    private readonly IAudioSessionService _audio;
    private readonly Func<AppSettings> _settings;
    private readonly Action _save;
    // Audio events come in bursts (one per session/channel): throttle them to one refresh per 60 ms.
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private DateTime _hiddenAt = DateTime.MinValue;
    private volatile bool _visible;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public FlyoutWindow(IAudioSessionService audio, Func<AppSettings> settings, Action save)
    {
        _audio = audio;
        _settings = settings;
        _save = save;
        InitializeComponent();
        DataContext = this;

        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Refresh(); };
        _audio.SessionsChanged += OnAudioChanged;
        _audio.VolumesChanged += OnAudioChanged;
        IsVisibleChanged += (_, e) => _visible = (bool)e.NewValue;
        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) => Reposition();
        AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler((_, e) => SetDragging(e.OriginalSource, true)));
        AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, e) => SetDragging(e.OriginalSource, false)));
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) HideFlyout(); };
    }

    /// <summary>Tells the row of the slider being dragged to ignore refreshes (coarse hardware steps made it fight the cursor).</summary>
    private static void SetDragging(object source, bool on)
    {
        if (source is not DependencyObject d) return;
        while (d is not null and not Slider) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        switch ((d as Slider)?.DataContext)
        {
            case AppVolumeViewModel app: app.Dragging = on; break;
            case DeviceViewModel dev: dev.Dragging = on; break;
        }
    }

    // Any thread. A hidden flyout does not even wake the UI thread.
    private void OnAudioChanged(object? sender, EventArgs e)
    {
        if (_visible) Dispatcher.BeginInvoke(() => _refreshDebounce.Start());
    }

    /// <summary>Toggles the flyout near the notification area.</summary>
    public void ShowNearTray()
    {
        if (IsVisible) { HideFlyout(); return; }
        // A tray click right after deactivation-hide should not immediately reopen it.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 500) return;
        Refresh();
        Show();
        Reposition();
        Activate();
        if (!IsActive) NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _refreshDebounce.Stop();
        _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    /// <summary>Diff live devices into the collection: add new, remove gone, sync existing.</summary>
    private void Refresh()
    {
        var live = _audio.GetDevices();
        var pinned = _settings().PinnedApps;
        var hidden = _settings().HiddenApps;
        for (var i = Devices.Count - 1; i >= 0; i--)
            if (!live.Any(d => d.Id == Devices[i].Id)) Devices.RemoveAt(i);
        for (var i = 0; i < live.Count; i++)
        {
            var index = IndexOf(live[i].Id);
            if (index < 0) Devices.Insert(Math.Min(i, Devices.Count), new DeviceViewModel(_audio, live[i], pinned, hidden));
            else
            {
                Devices[index].Sync(live[i], pinned, hidden);
                if (index != i && i < Devices.Count) Devices.Move(index, i); // default device first
            }
        }
        EmptyText.Visibility = Devices.All(d => d.Apps.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < Devices.Count; i++) if (Devices[i].Id == id) return i;
        return -1;
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DeviceViewModel device }) AudioPolicy.SetDefaultDevice(device.Id);
        // The device-change notification refreshes the list.
    }

    /// <summary>Right-click on an app: choose which output device it plays on.</summary>
    private void AppRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppVolumeViewModel app, ContextMenu: { } menu }) return;
        menu.Items.Clear();
        var pids = _audio.GetProcessIds(app.ExeName);
        var routed = pids.Select(AudioPolicy.GetAppDevice).FirstOrDefault(id => id is not null);

        var settings = _settings();
        var isPinned = settings.PinnedApps.Contains(app.ExeName, StringComparer.OrdinalIgnoreCase);
        var pin = new MenuItem { Header = isPinned ? "Unpin from the top" : "Pin to top" };
        pin.Click += (_, _) => SetPinned(app.ExeName, !isPinned);
        menu.Items.Add(pin);
        var hide = new MenuItem { Header = "Hide from the mixer", ToolTip = "It keeps working with shortcuts. Show it again in Settings." };
        hide.Click += (_, _) => HideApp(app.ExeName);
        menu.Items.Add(hide);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = $"Play {app.DisplayName} on", IsEnabled = false });
        var follow = new MenuItem { Header = "Default device", IsCheckable = true, IsChecked = routed is null };
        follow.Click += (_, _) => Route(pids, null);
        menu.Items.Add(follow);
        menu.Items.Add(new Separator());
        foreach (var device in Devices)
        {
            var item = new MenuItem { Header = device.Name, IsCheckable = true, IsChecked = routed == device.Id };
            var id = device.Id;
            item.Click += (_, _) => Route(pids, id);
            menu.Items.Add(item);
        }
    }

    private void SetPinned(string exe, bool pinned)
    {
        var list = _settings().PinnedApps;
        list.RemoveAll(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
        if (pinned) list.Add(Bindings.Target.NormalizeExe(exe));
        _save();
        Refresh();
    }

    private void HideApp(string exe)
    {
        var s = _settings();
        s.PinnedApps.RemoveAll(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
        if (!s.HiddenApps.Contains(exe, StringComparer.OrdinalIgnoreCase)) s.HiddenApps.Add(Bindings.Target.NormalizeExe(exe));
        _save();
        Refresh();
    }

    private static void Route(IReadOnlyList<int> pids, string? deviceId)
    {
        foreach (var pid in pids) AudioPolicy.SetAppDevice(pid, deviceId);
    }
}
