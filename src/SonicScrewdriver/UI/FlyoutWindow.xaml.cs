using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Input;
using SonicScrewdriver.Interop;
using SonicScrewdriver.Media;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

public partial class FlyoutWindow : Window
{
    private readonly IAudioSessionService _audio;
    private readonly Func<AppSettings> _settings;
    private readonly Action _save;
    private readonly IMediaSessionService _media;
    private readonly Func<IMicrophone?> _microphone;
    private MicViewModel? _mic;
    // Track changes come in bursts too: one now-playing read per 200 ms, and only while the mixer is open.
    private readonly DispatcherTimer _nowPlayingDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    // Audio events come in bursts (one per session/channel): throttle them to one refresh per 60 ms.
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private DateTime _hiddenAt = DateTime.MinValue;
    private volatile bool _visible;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public FlyoutWindow(IAudioSessionService audio, Func<AppSettings> settings, Action save, IMediaSessionService media, Func<IMicrophone?> microphone)
    {
        _microphone = microphone;
        _media = media;
        _audio = audio;
        _settings = settings;
        _save = save;
        InitializeComponent();
        DataContext = this;

        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Refresh(); };
        _audio.SessionsChanged += OnAudioChanged;
        _audio.VolumesChanged += OnAudioChanged;
        _audio.SessionsChanged += (_, _) => { if (_visible) Dispatcher.BeginInvoke(() => _nowPlayingDebounce.Start()); };
        _nowPlayingDebounce.Tick += async (_, _) => { _nowPlayingDebounce.Stop(); await UpdateNowPlayingAsync(); };
        IsVisibleChanged += (_, e) =>
        {
            _visible = (bool)e.NewValue;
            if (!_visible) { _nowPlayingDebounce.Stop(); _media.Watch(null); } // nothing is watched while the mixer is closed
        };
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
            case MicViewModel mic: mic.Dragging = on; break;
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
        (_cursor, _work) = MonitorArea.AtCursor(); // the monitor of the tray icon (the cursor is on it after a click)
        Show();
        StartNowPlaying();
        Reposition();
        Activate();
        if (!IsActive) NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    /// <summary>"Show what is playing" is on and competitive mode is off.</summary>
    private bool NowPlayingOn => _settings() is { NowPlayingInMixer: true, CompetitiveMode: false };

    private void StartNowPlaying()
    {
        if (!NowPlayingOn) { ClearNowPlaying(); return; }
        _media.Watch(() => Dispatcher.BeginInvoke(() => { if (_visible) _nowPlayingDebounce.Start(); }));
        _ = UpdateNowPlayingAsync();
    }

    private void ClearNowPlaying()
    {
        foreach (var app in Devices.SelectMany(d => d.Apps)) app.SetNowPlaying(null);
    }

    /// <summary>Reads the title and artist of each listed app once (the playing media session of that app).</summary>
    private async Task UpdateNowPlayingAsync()
    {
        if (!IsVisible) return;
        if (!NowPlayingOn) { ClearNowPlaying(); return; }
        var apps = Devices.SelectMany(d => d.Apps).ToList();
        foreach (var group in apps.GroupBy(a => a.ExeName, StringComparer.OrdinalIgnoreCase))
        {
            var info = await _media.GetNowPlayingAsync(group.Key);
            foreach (var app in group) app.SetNowPlaying(info);
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => Transport(sender, MediaKey.Previous);
    private void PlayPause_Click(object sender, RoutedEventArgs e) => Transport(sender, MediaKey.PlayPause);
    private void Next_Click(object sender, RoutedEventArgs e) => Transport(sender, MediaKey.Next);

    private void Transport(object sender, MediaKey key)
    {
        if (sender is FrameworkElement { Tag: AppVolumeViewModel app }) _ = _media.SendAsync(app.ExeName, key); // the media event refreshes the line
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _refreshDebounce.Stop();
        _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    private Point _cursor;
    private Rect _work = Rect.Empty;

    private void Reposition()
    {
        if (_work.IsEmpty) { var wa = SystemParameters.WorkArea; _work = wa; _cursor = new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2); }
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var at = PopupPlacement.Place(_cursor, _work, new Size(ActualWidth, ActualHeight), dpi.DpiScaleX, dpi.DpiScaleY, 12, cornerWhenInside: true);
        Left = at.X;
        Top = at.Y;
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
        RefreshMicrophone();
        EmptyText.Visibility = Devices.All(d => d.Apps.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The microphone section: shown only when the app hands over a microphone (feature on, competitive mode off, one plugged in).</summary>
    private void RefreshMicrophone()
    {
        var mic = _microphone();
        if (mic is not { Available: true })
        {
            _mic = null;
            MicSection.Visibility = Visibility.Collapsed;
            return;
        }
        if (_mic is null) { _mic = new MicViewModel(mic); MicSection.DataContext = _mic; }
        else _mic.Sync();
        MicSection.Visibility = Visibility.Visible;
    }

    /// <summary>The microphone changed (from anywhere): refresh while the mixer is open. UI thread.</summary>
    public void NotifyMicrophoneChanged()
    {
        if (_visible) _refreshDebounce.Start();
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
