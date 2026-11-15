using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SonicScrewdriver.Audio;

namespace SonicScrewdriver.UI;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One app row under a device. Setters push changes to that device's sessions.</summary>
public sealed class AppVolumeViewModel : ObservableObject
{
    private readonly IAudioSessionService _audio;
    private double _volume;
    private bool _muted;
    private ImageSource? _icon;

    public AppVolumeViewModel(IAudioSessionService audio, string deviceId, AudioApp app)
    {
        _audio = audio;
        DeviceId = deviceId;
        ExeName = app.ExeName;
        DisplayName = app.DisplayName;
        _icon = IconCache.Get(app.ExeName);
        _volume = Math.Round(app.Volume * 100);
        _muted = app.Muted;
    }

    public string DeviceId { get; }
    public string ExeName { get; }
    private string _displayName = "";

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (value == _displayName) return;
            _displayName = value;
            Raise();
        }
    }
    public ImageSource? Icon => _icon;

    public double Volume
    {
        get => _volume;
        set
        {
            var v = Math.Round(Math.Clamp(value, 0, 100));
            if (v == _volume) return;
            _volume = v;
            _audio.SetVolume(ExeName, (float)(v / 100), DeviceId);
            Raise();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (value == _muted) return;
            _muted = value;
            _audio.SetMute(ExeName, value, DeviceId); // set the clicked state, never toggle blindly
            Raise();
        }
    }

    /// <summary>Refresh from the service without writing back.</summary>
    public void Sync(AudioApp app)
    {
        DisplayName = app.DisplayName;
        var v = Math.Round(app.Volume * 100);
        if (v != _volume) { _volume = v; Raise(nameof(Volume)); }
        if (app.Muted != _muted) { _muted = app.Muted; Raise(nameof(Muted)); }
        if (_icon is null && IconCache.Get(ExeName) is { } icon) { _icon = icon; Raise(nameof(Icon)); }
    }
}

/// <summary>An output device: its own volume plus the apps playing on it.</summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly IAudioSessionService _audio;
    private double _volume;
    private bool _muted;
    private bool _isDefault;

    public DeviceViewModel(IAudioSessionService audio, AudioDevice device)
    {
        _audio = audio;
        Id = device.Id;
        Name = device.Name;
        Sync(device);
    }

    public string Id { get; }
    public string Name { get; }
    public ObservableCollection<AppVolumeViewModel> Apps { get; } = new();

    public bool IsDefault
    {
        get => _isDefault;
        private set { if (value != _isDefault) { _isDefault = value; Raise(); } }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            var v = Math.Round(Math.Clamp(value, 0, 100));
            if (v == _volume) return;
            _volume = v;
            _audio.SetDeviceVolume(Id, (float)(v / 100));
            Raise();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (value == _muted) return;
            _muted = value;
            _audio.SetDeviceMute(Id, value);
            Raise();
        }
    }

    /// <summary>Diff the device state and its app rows in place (no flicker, no lost slider drag).</summary>
    public void Sync(AudioDevice device)
    {
        IsDefault = device.IsDefault;
        var v = Math.Round(device.Volume * 100);
        if (v != _volume) { _volume = v; Raise(nameof(Volume)); }
        if (device.Muted != _muted) { _muted = device.Muted; Raise(nameof(Muted)); }

        var live = device.Apps;
        for (var i = Apps.Count - 1; i >= 0; i--)
            if (!live.Any(a => string.Equals(a.ExeName, Apps[i].ExeName, StringComparison.OrdinalIgnoreCase)))
                Apps.RemoveAt(i);
        for (var i = 0; i < live.Count; i++)
        {
            var existing = Apps.FirstOrDefault(a => string.Equals(a.ExeName, live[i].ExeName, StringComparison.OrdinalIgnoreCase));
            if (existing is null) Apps.Insert(Math.Min(i, Apps.Count), new AppVolumeViewModel(_audio, Id, live[i]));
            else existing.Sync(live[i]);
        }
    }
}
