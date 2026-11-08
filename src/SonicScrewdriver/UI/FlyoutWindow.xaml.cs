using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Interop;

namespace SonicScrewdriver.UI;

public partial class FlyoutWindow : Window
{
    private readonly IAudioSessionService _audio;
    // Audio events come in bursts (one per session/channel): throttle them to one refresh per 60 ms.
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private DateTime _hiddenAt = DateTime.MinValue;
    private volatile bool _visible;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public FlyoutWindow(IAudioSessionService audio)
    {
        _audio = audio;
        InitializeComponent();
        DataContext = this;

        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Refresh(); };
        _audio.SessionsChanged += OnAudioChanged;
        _audio.VolumesChanged += OnAudioChanged;
        IsVisibleChanged += (_, e) => _visible = (bool)e.NewValue;
        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) => Reposition();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) HideFlyout(); };
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
        for (var i = Devices.Count - 1; i >= 0; i--)
            if (!live.Any(d => d.Id == Devices[i].Id)) Devices.RemoveAt(i);
        for (var i = 0; i < live.Count; i++)
        {
            var index = IndexOf(live[i].Id);
            if (index < 0) Devices.Insert(Math.Min(i, Devices.Count), new DeviceViewModel(_audio, live[i]));
            else
            {
                Devices[index].Sync(live[i]);
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

    private static void Route(IReadOnlyList<int> pids, string? deviceId)
    {
        foreach (var pid in pids) AudioPolicy.SetAppDevice(pid, deviceId);
    }
}
