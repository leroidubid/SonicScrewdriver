using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SonicScrewdriver.Audio;

/// <summary>
/// WASAPI implementation over all active render devices. Sessions are enumerated once per device and
/// then tracked through Windows notifications, so key presses and the flyout never re-enumerate.
/// COM callbacks only queue work and raise events; all COM calls on sessions happen on the caller
/// (UI) thread. Use from the UI thread.
/// </summary>
public sealed class AudioSessionService : IAudioSessionService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceNotifications _notifications;
    private List<DeviceTracker> _devices = new();
    private string? _defaultId;
    private volatile bool _dirty = true;

    public event EventHandler? SessionsChanged;
    public event EventHandler? VolumesChanged;

    public AudioSessionService()
    {
        _notifications = new DeviceNotifications(() =>
        {
            _dirty = true;
            RaiseSessionsChanged();
        });
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
    }

    private void RaiseSessionsChanged() => SessionsChanged?.Invoke(this, EventArgs.Empty);
    private void RaiseVolumesChanged() => VolumesChanged?.Invoke(this, EventArgs.Empty);

    private List<DeviceTracker> Devices
    {
        get
        {
            if (_dirty) Rebuild();
            return _devices;
        }
    }

    private void Rebuild()
    {
        foreach (var d in _devices) d.Dispose();
        _devices = new List<DeviceTracker>();
        _defaultId = null;
        _dirty = false; // cleared first: a device event arriving during the rebuild sets it again
        try
        {
            try
            {
                using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _defaultId = def.ID;
            }
            catch (Exception) { /* no default device */ }

            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try { _devices.Add(new DeviceTracker(device, RaiseSessionsChanged, RaiseVolumesChanged)); }
                catch (Exception) { try { device.Dispose(); } catch (Exception) { } }
            }
        }
        catch (Exception) { _dirty = true; }
    }

    private DeviceTracker? DefaultDevice => Devices.FirstOrDefault(d => d.Id == _defaultId);

    private IEnumerable<SessionEntry> Sessions(string exe, string? deviceId = null) =>
        Devices.Where(d => deviceId is null || d.Id == deviceId)
            .SelectMany(d => d.Snapshot())
            .Where(s => string.Equals(s.Exe, exe, StringComparison.OrdinalIgnoreCase));

    private static List<AudioApp> GroupApps(IEnumerable<SessionEntry> sessions)
    {
        var apps = new List<AudioApp>();
        foreach (var g in sessions.GroupBy(s => s.Exe, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                apps.Add(new AudioApp(g.Key, ProcessInfo.DisplayName(g.Key), g.First().Pid,
                    g.Max(s => s.Control.SimpleAudioVolume.Volume), g.All(s => s.Control.SimpleAudioVolume.Mute)));
            }
            catch (Exception) { /* session vanished mid-read */ }
        }
        return apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IReadOnlyList<AudioApp> GetApps() => GroupApps(Devices.SelectMany(d => d.Snapshot()));

    public IReadOnlyList<AudioDevice> GetDevices()
    {
        var list = new List<AudioDevice>();
        foreach (var d in Devices)
        {
            try
            {
                var ep = d.Device.AudioEndpointVolume;
                list.Add(new AudioDevice(d.Id, d.Name, d.Id == _defaultId, ep.MasterVolumeLevelScalar, ep.Mute, GroupApps(d.Snapshot())));
            }
            catch (Exception) { _dirty = true; }
        }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IReadOnlyList<int> GetProcessIds(string exe) => Sessions(exe).Select(s => s.Pid).Distinct().ToList();

    public float? GetVolume(string exe)
    {
        try
        {
            var vols = Sessions(exe).Select(s => s.Control.SimpleAudioVolume.Volume).ToList();
            return vols.Count == 0 ? null : vols.Max();
        }
        catch (Exception) { return null; }
    }

    public bool SetVolume(string exe, float volume, string? deviceId = null)
    {
        var v = Math.Clamp(volume, 0f, 1f);
        var ok = false;
        foreach (var s in Sessions(exe, deviceId))
        {
            try { s.Control.SimpleAudioVolume.Volume = v; ok = true; } catch (Exception) { }
        }
        return ok;
    }

    public bool? GetMute(string exe)
    {
        try
        {
            var mutes = Sessions(exe).Select(s => s.Control.SimpleAudioVolume.Mute).ToList();
            return mutes.Count == 0 ? null : mutes.All(m => m);
        }
        catch (Exception) { return null; }
    }

    public bool? ToggleMute(string exe)
    {
        var current = GetMute(exe);
        if (current is null) return null;
        return SetMute(exe, !current.Value) ? !current.Value : null;
    }

    public bool SetMute(string exe, bool muted, string? deviceId = null)
    {
        var ok = false;
        foreach (var s in Sessions(exe, deviceId))
        {
            try { s.Control.SimpleAudioVolume.Mute = muted; ok = true; } catch (Exception) { }
        }
        return ok;
    }

    public float MasterVolume
    {
        get { try { return DefaultDevice?.Device.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f; } catch (Exception) { return 0f; } }
        set { if (DefaultDevice is { } d) SetDeviceVolume(d.Id, value); }
    }

    public bool MasterMuted
    {
        get { try { return DefaultDevice?.Device.AudioEndpointVolume.Mute ?? false; } catch (Exception) { return false; } }
        set { if (DefaultDevice is { } d) SetDeviceMute(d.Id, value); }
    }

    public void SetDeviceVolume(string deviceId, float volume)
    {
        try { if (Devices.FirstOrDefault(d => d.Id == deviceId) is { } d) d.Device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f); }
        catch (Exception) { }
    }

    public void SetDeviceMute(string deviceId, bool muted)
    {
        try { if (Devices.FirstOrDefault(d => d.Id == deviceId) is { } d) d.Device.AudioEndpointVolume.Mute = muted; }
        catch (Exception) { }
    }

    public bool SetDefaultDevice(string deviceId)
    {
        var ok = AudioPolicy.SetDefaultDevice(deviceId);
        if (ok) _dirty = true; // read the new default now, without waiting for the notification
        return ok;
    }

    public string GetDisplayName(string exe) => ProcessInfo.DisplayName(exe);

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch (Exception) { }
        foreach (var d in _devices) d.Dispose();
        _devices.Clear();
        _enumerator.Dispose();
    }

    /// <summary>One output device and its live sessions.</summary>
    private sealed class DeviceTracker : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<SessionEntry> _sessions = new();
        private readonly List<IAudioSessionControl> _pending = new();
        private readonly Action _sessionsChanged;
        private readonly Action _volumesChanged;
        private readonly AudioSessionManager _manager;
        private readonly AudioEndpointVolume _endpoint;

        public MMDevice Device { get; }
        public string Id { get; }
        public string Name { get; }

        public DeviceTracker(MMDevice device, Action sessionsChanged, Action volumesChanged)
        {
            Device = device;
            Id = device.ID;
            Name = device.FriendlyName;
            _sessionsChanged = sessionsChanged;
            _volumesChanged = volumesChanged;

            _endpoint = device.AudioEndpointVolume;
            _endpoint.OnVolumeNotification += OnEndpointVolume;

            _manager = device.AudioSessionManager;
            _manager.OnSessionCreated += OnSessionCreated;
            _manager.RefreshSessions(); // also arms session-created notifications
            var all = _manager.Sessions;
            for (var i = 0; i < all.Count; i++) { var index = i; TryAdd(() => all[index]); }
        }

        private void OnEndpointVolume(AudioVolumeNotificationData data) => _volumesChanged();

        // COM callback thread: only queue; registration happens on the caller thread in Snapshot().
        private void OnSessionCreated(object sender, IAudioSessionControl session)
        {
            lock (_lock) _pending.Add(session);
            _sessionsChanged();
        }

        private void TryAdd(Func<AudioSessionControl> create)
        {
            AudioSessionControl? control = null;
            System.Diagnostics.Process? process = null;
            try
            {
                control = create();
                if (control.IsSystemSoundsSession || control.State == AudioSessionState.AudioSessionStateExpired) { control.Dispose(); return; }
                var pid = (int)control.GetProcessID;
                if (pid == 0) { control.Dispose(); return; }
                process = System.Diagnostics.Process.GetProcessById(pid);
                var exe = Bindings.Target.NormalizeExe(process.ProcessName);
                ProcessInfo.Remember(pid, exe);
                var entry = new SessionEntry(control, process, exe, pid, _sessionsChanged, _volumesChanged);
                control.RegisterEventClient(entry);
                lock (_lock) _sessions.Add(entry);
            }
            catch (Exception)
            {
                try { control?.Dispose(); } catch (Exception) { }
                process?.Dispose();
            }
        }

        /// <summary>Live sessions. Adds queued new sessions and prunes ended ones (on the caller thread).</summary>
        public List<SessionEntry> Snapshot()
        {
            List<IAudioSessionControl> pending;
            List<SessionEntry> dead;
            lock (_lock)
            {
                pending = _pending.ToList();
                _pending.Clear();
                // A closed app's session often goes Inactive, not Expired: also drop exited processes.
                dead = _sessions.Where(s => s.Ended || s.ProcessExited).ToList();
                _sessions.RemoveAll(dead.Contains);
            }
            foreach (var d in dead) d.Release();
            foreach (var p in pending) TryAdd(() => new AudioSessionControl(p));
            lock (_lock) return _sessions.ToList();
        }

        public void Dispose()
        {
            try { _manager.OnSessionCreated -= OnSessionCreated; } catch (Exception) { }
            try { _endpoint.OnVolumeNotification -= OnEndpointVolume; } catch (Exception) { }
            List<SessionEntry> all;
            lock (_lock) { all = _sessions.ToList(); _sessions.Clear(); _pending.Clear(); }
            foreach (var s in all) s.Release();
            try { Device.Dispose(); } catch (Exception) { }
        }
    }

    /// <summary>A session plus its event sink. Callbacks only flag and notify.</summary>
    private sealed class SessionEntry : IAudioSessionEventsHandler
    {
        private readonly Action _sessionsChanged;
        private readonly Action _volumesChanged;

        private readonly System.Diagnostics.Process _process;

        public AudioSessionControl Control { get; }
        public string Exe { get; }
        public int Pid { get; }
        public volatile bool Ended;

        public bool ProcessExited
        {
            get { try { return _process.HasExited; } catch (Exception) { return true; } }
        }

        public SessionEntry(AudioSessionControl control, System.Diagnostics.Process process, string exe, int pid, Action sessionsChanged, Action volumesChanged)
        {
            Control = control;
            _process = process;
            Exe = exe;
            Pid = pid;
            _sessionsChanged = sessionsChanged;
            _volumesChanged = volumesChanged;
        }

        public void Release()
        {
            try { Control.UnRegisterEventClient(this); } catch (Exception) { }
            try { Control.Dispose(); } catch (Exception) { }
            _process.Dispose();
        }

        public void OnVolumeChanged(float volume, bool isMuted) => _volumesChanged();
        public void OnStateChanged(AudioSessionState state)
        {
            if (state == AudioSessionState.AudioSessionStateExpired) Ended = true;
            if (state != AudioSessionState.AudioSessionStateActive) _sessionsChanged(); // lets Snapshot prune exited apps
        }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { Ended = true; _sessionsChanged(); }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
    }

    private sealed class DeviceNotifications : IMMNotificationClient
    {
        private readonly Action _onChange;
        public DeviceNotifications(Action onChange) => _onChange = onChange;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) _onChange();
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChange();
        public void OnDeviceAdded(string pwstrDeviceId) => _onChange();
        public void OnDeviceRemoved(string deviceId) => _onChange();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
