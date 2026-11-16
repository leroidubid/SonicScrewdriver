using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SonicScrewdriver.Audio;

/// <summary>The default microphone: its name, volume (0..1) and mute. <see cref="Available"/> is false when there is none.</summary>
public interface IMicrophone
{
    bool Available { get; }
    string Name { get; }
    float Volume { get; set; }
    bool Muted { get; set; }
}

public static class MicrophoneToggle
{
    /// <summary>Mutes the microphone, or unmutes it. Returns the new state (true = muted), or null when there is no microphone.</summary>
    public static bool? Toggle(IMicrophone mic)
    {
        if (!mic.Available) return null;
        var muted = !mic.Muted;
        mic.Muted = muted;
        return muted;
    }
}

/// <summary>
/// The default capture device through WASAPI. It is created only while a microphone feature is in use (the shortcut or
/// the mixer section) and watches its volume and mute (and the default device) through Windows notifications: no polling.
/// Callbacks only flag and raise <see cref="Changed"/>; the COM calls happen on the caller thread (UI).
/// </summary>
public sealed class MicrophoneService : IMicrophone, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Notifications _notifications;
    private MMDevice? _device;
    private AudioEndpointVolume? _endpoint;
    private volatile bool _dirty = true;

    /// <summary>The volume or mute changed, or the default microphone did. Raised on any thread.</summary>
    public event EventHandler? Changed;

    public MicrophoneService()
    {
        _notifications = new Notifications(() => { _dirty = true; Changed?.Invoke(this, EventArgs.Empty); });
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
    }

    private AudioEndpointVolume? Endpoint
    {
        get
        {
            if (_dirty) Attach();
            return _endpoint;
        }
    }

    private void Attach()
    {
        _dirty = false;
        Detach();
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            _endpoint = _device.AudioEndpointVolume;
            _endpoint.OnVolumeNotification += OnVolume;
        }
        catch (Exception) { Detach(); } // no microphone
    }

    private void Detach()
    {
        try { if (_endpoint is not null) _endpoint.OnVolumeNotification -= OnVolume; } catch (Exception) { }
        try { _endpoint?.Dispose(); } catch (Exception) { }
        try { _device?.Dispose(); } catch (Exception) { }
        _endpoint = null;
        _device = null;
    }

    private void OnVolume(AudioVolumeNotificationData data) => Changed?.Invoke(this, EventArgs.Empty);

    public bool Available => Endpoint is not null;

    public string Name
    {
        get { try { _ = Endpoint; return _device?.FriendlyName ?? "Microphone"; } catch (Exception) { return "Microphone"; } }
    }

    public float Volume
    {
        get { try { return Endpoint?.MasterVolumeLevelScalar ?? 0f; } catch (Exception) { return 0f; } }
        set { try { if (Endpoint is { } e) e.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); } catch (Exception) { } }
    }

    public bool Muted
    {
        get { try { return Endpoint?.Mute ?? false; } catch (Exception) { return false; } }
        set { try { if (Endpoint is { } e) e.Mute = value; } catch (Exception) { } }
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch (Exception) { }
        Detach();
        _enumerator.Dispose();
    }

    private sealed class Notifications : IMMNotificationClient
    {
        private readonly Action _onChange;
        public Notifications(Action onChange) => _onChange = onChange;
        private static bool IsCapture(string id) => id.StartsWith("{0.0.1.", StringComparison.Ordinal);
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Capture) _onChange(); }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { if (IsCapture(deviceId)) _onChange(); }
        public void OnDeviceAdded(string pwstrDeviceId) { if (IsCapture(pwstrDeviceId)) _onChange(); }
        public void OnDeviceRemoved(string deviceId) { if (IsCapture(deviceId)) _onChange(); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
