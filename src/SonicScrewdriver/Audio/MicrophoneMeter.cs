using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SonicScrewdriver.Audio;

/// <summary>
/// The peak level (0..1) of the default capture device, for streamer mode. It is created only while the mode is on and an
/// app is ticked. The device is attached on the first read and again after Windows reports a change (unplugged, new
/// default microphone); a device that fails reads as "no level" (null) and is retried at the next read, never a crash.
/// Always read from one thread (the UI timer); the notification callback only raises a flag.
/// </summary>
public sealed class MicrophoneMeter : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Notifications _notifications;
    private MMDevice? _device;
    private volatile bool _dirty = true;
    private long _retryAt;

    public MicrophoneMeter()
    {
        _notifications = new Notifications(() => _dirty = true);
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
    }

    /// <summary>The current peak, or null when there is no microphone or it cannot be read.</summary>
    public float? Peak()
    {
        try
        {
            if (_dirty)
            {
                // After a failed attach, wait a second before trying again (a missing microphone costs nothing meanwhile).
                if (Environment.TickCount64 < _retryAt) return null;
                _dirty = false;
                Detach();
                try { _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
                catch (Exception) { _retryAt = Environment.TickCount64 + 1000; _dirty = true; return null; }
            }
            return _device?.AudioMeterInformation.MasterPeakValue;
        }
        catch (Exception)
        {
            Detach();
            _dirty = true;
            _retryAt = Environment.TickCount64 + 1000;
            return null;
        }
    }

    private void Detach()
    {
        try { _device?.Dispose(); } catch (Exception) { }
        _device = null;
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch (Exception) { }
        Detach();
        try { _enumerator.Dispose(); } catch (Exception) { }
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
