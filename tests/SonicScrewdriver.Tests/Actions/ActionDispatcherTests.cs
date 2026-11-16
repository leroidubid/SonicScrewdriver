using System.IO;
using SonicScrewdriver.Actions;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Media;
using SonicScrewdriver.UI;

namespace SonicScrewdriver.Tests.Actions;

internal sealed class FakeAudio : IAudioSessionService
{
    public Dictionary<string, (float Vol, bool Muted)> Apps { get; } = new();
    public HashSet<string> FailSets { get; } = new();
    public List<(string Exe, float Vol)> SetCalls { get; } = new();

    public IReadOnlyList<AudioApp> GetApps() =>
        Apps.Select(kv => new AudioApp(kv.Key, GetDisplayName(kv.Key), 1, kv.Value.Vol, kv.Value.Muted)).ToList();
    public VolumeLimits Limits { get; } = new();
    public Dictionary<string, float> Peaks { get; } = new();
    public float? GetPeak(string exe) => Apps.ContainsKey(exe) ? Peaks.GetValueOrDefault(exe) : null;
    public float? GetVolume(string exe) => Apps.TryGetValue(exe, out var a) ? a.Vol : null;
    public List<AudioDevice>? DeviceList { get; set; }
    public List<string> DefaultCalls { get; } = new();
    public IReadOnlyList<AudioDevice> GetDevices() => DeviceList ?? new List<AudioDevice> { new("dev", "Speakers", true, MasterVolume, MasterMuted, GetApps()) };
    public bool SetDefaultDevice(string deviceId) { DefaultCalls.Add(deviceId); return true; }
    public IReadOnlyList<int> GetProcessIds(string exe) => Apps.ContainsKey(exe) ? new[] { 1 } : Array.Empty<int>();
    public List<(string Exe, bool Muted, string? Device)> MuteCalls { get; } = new();
    public bool SetMute(string exe, bool muted, string? deviceId = null)
    {
        MuteCalls.Add((exe, muted, deviceId));
        if (!Apps.TryGetValue(exe, out var a)) return false;
        Apps[exe] = (a.Vol, muted);
        return true;
    }
    public void SetDeviceVolume(string deviceId, float volume) => MasterVolume = volume;
    public void SetDeviceMute(string deviceId, bool muted) => MasterMuted = muted;
    public event EventHandler? VolumesChanged { add { } remove { } }
    public bool SetVolume(string exe, float volume, string? deviceId = null)
    {
        if (!Apps.ContainsKey(exe) || FailSets.Contains(exe)) return false;
        volume = Limits.Clamp(exe, volume);
        SetCalls.Add((exe, volume));
        Apps[exe] = (volume, Apps[exe].Muted);
        return true;
    }
    public bool? ToggleMute(string exe)
    {
        if (!Apps.TryGetValue(exe, out var a)) return null;
        Apps[exe] = (a.Vol, !a.Muted);
        return !a.Muted;
    }
    public bool? GetMute(string exe) => Apps.TryGetValue(exe, out var a) ? a.Muted : null;
    public float MasterVolume { get; set; } = 0.5f;
    public bool MasterMuted { get; set; }
    public event EventHandler? SessionsChanged { add { } remove { } }
    public string GetDisplayName(string exe) => exe switch
    {
        "spotify.exe" => "Spotify",
        _ => Path.GetFileNameWithoutExtension(exe),
    };
}

internal sealed class FakeMedia : IMediaSessionService
{
    public MediaResult Result { get; set; } = MediaResult.Ok;
    public List<(string Exe, MediaKey Key)> Calls { get; } = new();
    public NowPlaying? Playing { get; set; }
    public List<Action?> Watchers { get; } = new();
    public Task<NowPlaying?> GetNowPlayingAsync(string exe) => Task.FromResult(Playing);
    public void Watch(Action? onChanged) => Watchers.Add(onChanged);
    public Task<MediaResult> SendAsync(string exe, MediaKey key)
    {
        Calls.Add((exe, key));
        return Task.FromResult(Result);
    }
}

internal sealed class FakeForeground : IForegroundProcess
{
    public string? Exe { get; set; }
    public string? GetForegroundExe() => Exe;
}

internal sealed class FakeOsd : IOsd
{
    public (string Title, string Detail)? Last { get; private set; }
    public string? LastExe { get; private set; }
    public void Show(string? exe, string title, string detail) { LastExe = exe; Last = (title, detail); }
}

public class ActionDispatcherTests
{
    private readonly FakeAudio _audio = new();
    private readonly FakeMedia _media = new();
    private readonly FakeForeground _fg = new();
    private readonly FakeOsd _osd = new();
    private ActionDispatcher Sut => new(_audio, _media, _fg, _osd);
    private static readonly KeyBinding Spotify = new(Modifiers.Shift, Target.Process("spotify"), 5);
    private static readonly KeyBinding Fg = new(Modifiers.Ctrl, Target.Foreground, 5);

    private static readonly KeyBinding Out = new(Modifiers.Alt, Target.OutputDevice, 5);

    private void TwoDevices() => _audio.DeviceList = new()
    {
        new("hs", "Headset", true, 0.5f, false, Array.Empty<AudioApp>()),
        new("sp", "Speakers", false, 0.3f, false, Array.Empty<AudioApp>()),
    };

    [Fact]
    public async Task VolumeUp_on_output_device_target_switches_default_device()
    {
        TwoDevices();
        await Sut.ExecuteAsync(Out, MediaKey.VolumeUp);
        Assert.Equal("sp", Assert.Single(_audio.DefaultCalls));
        Assert.Equal(("Speakers", "Default output"), _osd.Last);
    }

    [Fact]
    public async Task Cycle_respects_selected_devices()
    {
        TwoDevices();
        var sut = Sut;
        sut.CycleDeviceIds = new[] { "hs" };
        await sut.ExecuteAsync(Out, MediaKey.VolumeDown);
        Assert.Empty(_audio.DefaultCalls); // only the current default is selected: nothing to switch to
        Assert.Equal(("Headset", "Default output"), _osd.Last);
    }

    [Fact]
    public async Task Next_on_output_device_target_only_shows_current_device()
    {
        TwoDevices();
        await Sut.ExecuteAsync(Out, MediaKey.Next);
        Assert.Empty(_audio.DefaultCalls);
        Assert.Equal(("Headset", "Default output"), _osd.Last);
    }

    [Fact]
    public async Task VolumeDown_on_process_target_sets_volume_and_shows_osd()
    {
        _audio.Apps["spotify.exe"] = (0.50f, false);
        await Sut.ExecuteAsync(Spotify, MediaKey.VolumeDown);
        var call = Assert.Single(_audio.SetCalls);
        Assert.Equal("spotify.exe", call.Exe);
        Assert.Equal(0.45f, call.Vol, 3);
        Assert.Equal(("Spotify", "45 %"), _osd.Last);
        Assert.Equal("spotify.exe", _osd.LastExe);
    }

    [Fact]
    public async Task Foreground_target_uses_foreground_exe()
    {
        _audio.Apps["chrome.exe"] = (0.30f, false);
        _fg.Exe = "chrome.exe";
        await Sut.ExecuteAsync(Fg, MediaKey.VolumeUp);
        Assert.Equal(0.35f, _audio.Apps["chrome.exe"].Vol, 3);
    }

    [Fact]
    public async Task Foreground_without_audio_shows_no_sound_control()
    {
        _fg.Exe = "explorer.exe";
        await Sut.ExecuteAsync(Fg, MediaKey.VolumeUp);
        Assert.Empty(_audio.SetCalls);
        Assert.Equal(("explorer", "No sound control"), _osd.Last);
    }

    [Fact]
    public async Task Foreground_null_shows_no_sound_control()
    {
        await Sut.ExecuteAsync(Fg, MediaKey.Mute);
        Assert.Equal(("Foreground app", "No sound control"), _osd.Last);
        Assert.Null(_osd.LastExe);
    }

    [Theory]
    [InlineData(MediaKey.VolumeUp)]
    [InlineData(MediaKey.VolumeDown)]
    public async Task Changing_volume_unmutes_a_muted_app(MediaKey key)
    {
        _audio.Apps["spotify.exe"] = (0.50f, true);
        await Sut.ExecuteAsync(Spotify, key);
        Assert.False(_audio.Apps["spotify.exe"].Muted);
    }

    [Fact]
    public async Task Mute_toggles()
    {
        _audio.Apps["spotify.exe"] = (0.50f, false);
        await Sut.ExecuteAsync(Spotify, MediaKey.Mute);
        Assert.True(_audio.Apps["spotify.exe"].Muted);
        Assert.Equal(("Spotify", "Muted"), _osd.Last);
        await Sut.ExecuteAsync(Spotify, MediaKey.Mute);
        Assert.Equal(("Spotify", "Unmuted"), _osd.Last);
    }

    [Theory]
    [InlineData(MediaKey.PlayPause, "⏯")]
    [InlineData(MediaKey.Next, "⏭")]
    [InlineData(MediaKey.Previous, "⏮")]
    public async Task Media_keys_are_sent_to_target(MediaKey key, string glyph)
    {
        await Sut.ExecuteAsync(Spotify, key);
        Assert.Equal(("spotify.exe", key), Assert.Single(_media.Calls));
        Assert.Equal(("Spotify", glyph), _osd.Last);
    }

    [Fact]
    public async Task PlayPause_without_media_session_reports_unavailable()
    {
        _media.Result = MediaResult.NoSession;
        await Sut.ExecuteAsync(Spotify, MediaKey.PlayPause);
        Assert.Equal(("Spotify", "media control unavailable"), _osd.Last);
    }

    [Fact]
    public async Task Media_failure_reports_error()
    {
        _media.Result = MediaResult.Failed;
        await Sut.ExecuteAsync(Spotify, MediaKey.Next);
        Assert.Equal(("Spotify", "error"), _osd.Last);
    }
}
