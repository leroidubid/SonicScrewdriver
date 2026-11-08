using System.Globalization;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Media;
using SonicScrewdriver.UI;

namespace SonicScrewdriver.Actions;

/// <summary>Executes a media key against a binding's target and reports the result on the OSD.</summary>
public sealed class ActionDispatcher
{
    private readonly IAudioSessionService _audio;
    private readonly IMediaSessionService _media;
    private readonly IForegroundProcess _foreground;
    private readonly IOsd _osd;

    public ActionDispatcher(IAudioSessionService audio, IMediaSessionService media, IForegroundProcess foreground, IOsd osd)
    {
        _audio = audio;
        _media = media;
        _foreground = foreground;
        _osd = osd;
    }

    /// <summary>Devices included in the output-device cycle; empty means every active device.</summary>
    public IReadOnlyCollection<string> CycleDeviceIds { get; set; } = Array.Empty<string>();

    public async Task ExecuteAsync(KeyBinding binding, MediaKey key)
    {
        if (binding.Target.Kind == TargetKind.OutputDevice)
        {
            ExecuteOnOutputDevice(binding, key);
            return;
        }

        var exe = binding.Target.Kind == TargetKind.Foreground ? _foreground.GetForegroundExe() : binding.Target.ExeName;
        if (string.IsNullOrEmpty(exe))
        {
            _osd.Show(null, "Foreground app", "No sound control");
            return;
        }

        var title = _audio.GetDisplayName(exe);
        switch (key)
        {
            case MediaKey.VolumeUp:
            case MediaKey.VolumeDown:
                var current = _audio.GetVolume(exe);
                if (current is null) { _osd.Show(exe, title, "No sound control"); return; }
                var next = VolumeMath.Apply(current.Value, key, binding.StepPercent);
                if (!_audio.SetVolume(exe, next)) { _osd.Show(exe, title, "No sound control"); return; }
                if (_audio.GetMute(exe) == true) _audio.SetMute(exe, false); // like Windows: changing volume unmutes
                _osd.Show(exe, title, FormatPercent(next));
                break;

            case MediaKey.Mute:
                var muted = _audio.ToggleMute(exe);
                _osd.Show(exe, title, muted switch { null => "No sound control", true => "Muted", false => "Unmuted" });
                break;

            default:
                var result = await _media.SendAsync(exe, key);
                _osd.Show(exe, title, result switch
                {
                    MediaResult.Ok => Glyph(key),
                    MediaResult.NoSession => "media control unavailable",
                    _ => "error",
                });
                break;
        }
    }

    private void ExecuteOnOutputDevice(KeyBinding binding, MediaKey key)
    {
        var devices = _audio.GetDevices();
        var current = devices.FirstOrDefault(d => d.IsDefault);
        switch (key)
        {
            case MediaKey.VolumeUp:
            case MediaKey.VolumeDown:
                var nextId = DeviceCycle.Next(devices.Select(d => d.Id).ToList(), current?.Id, CycleDeviceIds,
                    key == MediaKey.VolumeUp ? 1 : -1);
                var next = devices.FirstOrDefault(d => d.Id == nextId);
                if (next is null) { _osd.Show(null, "Output device", "No output device"); return; }
                if (next.Id != current?.Id && !_audio.SetDefaultDevice(next.Id)) { _osd.Show(null, next.Name, "error"); return; }
                _osd.Show(null, next.Name, "Default output");
                break;

            case MediaKey.Mute:
                var muted = !_audio.MasterMuted;
                _audio.MasterMuted = muted;
                _osd.Show(null, current?.Name ?? "Output device", muted ? "Muted" : "Unmuted");
                break;

            default: // Play/Pause, Next, Previous: show which output is active
                _osd.Show(null, current?.Name ?? "Output device", current is null ? "No output device" : "Default output");
                break;
        }
    }

    private static string FormatPercent(float v) =>
        ((int)MathF.Round(v * 100)).ToString(CultureInfo.InvariantCulture) + " %";

    private static string Glyph(MediaKey key) => key switch
    {
        MediaKey.PlayPause => "⏯",
        MediaKey.Next => "⏭",
        _ => "⏮",
    };
}
