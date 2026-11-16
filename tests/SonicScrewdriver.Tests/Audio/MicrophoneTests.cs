using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;
using SonicScrewdriver.UI;

namespace SonicScrewdriver.Tests.Audio;

public class MicrophoneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public MicrophoneTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeMic : IMicrophone
    {
        public bool Available { get; set; } = true;
        public string Name { get; set; } = "Headset mic";
        public float Volume { get; set; } = 0.6f;
        public bool Muted { get; set; }
    }

    [Fact]
    public void Toggle_mutes_then_unmutes_and_reports_the_new_state()
    {
        var mic = new FakeMic();
        Assert.True(MicrophoneToggle.Toggle(mic));
        Assert.True(mic.Muted);
        Assert.False(MicrophoneToggle.Toggle(mic));
        Assert.False(mic.Muted);
        Assert.Equal(0.6f, mic.Volume); // the level is not touched
    }

    [Fact]
    public void Toggle_without_a_microphone_does_nothing()
    {
        var mic = new FakeMic { Available = false };
        Assert.Null(MicrophoneToggle.Toggle(mic));
        Assert.False(mic.Muted);
    }

    [Fact]
    public void Mixer_row_writes_to_the_microphone_and_follows_it()
    {
        var mic = new FakeMic();
        var vm = new MicViewModel(mic);
        Assert.Equal(60, vm.Volume);
        vm.Volume = 150;                       // clamped
        Assert.Equal(1f, mic.Volume);
        vm.Muted = true;
        Assert.True(mic.Muted);
        mic.Volume = 0.25f; mic.Muted = false; // changed by someone else
        vm.Sync();
        Assert.Equal(25, vm.Volume);
        Assert.False(vm.Muted);
        Assert.Equal(0.25f, mic.Volume);       // syncing never writes back
    }

    [Fact]
    public void Row_ignores_a_refresh_while_the_slider_is_dragged()
    {
        var mic = new FakeMic();
        var vm = new MicViewModel(mic) { Dragging = true };
        mic.Volume = 0.1f;
        vm.Sync();
        Assert.Equal(60, vm.Volume);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Real_service_reads_the_default_microphone_without_changing_it()
    {
        using var mic = new MicrophoneService();
        if (!mic.Available) return; // no microphone on this machine
        Assert.InRange(mic.Volume, 0f, 1f);
        Assert.False(string.IsNullOrEmpty(mic.Name));
        var muted = mic.Muted;
        Assert.Equal(muted, mic.Muted);
    }

    [Fact]
    public void Settings_default_off_and_validate_the_shortcut()
    {
        var s = new AppSettings();
        Assert.Null(s.MicMuteHotkey);
        Assert.False(s.MicrophoneInMixer);
        Assert.Empty(new AppSettings { MicMuteHotkey = "Ctrl+Alt+K" }.Validate());
        Assert.NotEmpty(new AppSettings { MicMuteHotkey = "K" }.Validate());
        Assert.NotEmpty(new AppSettings { MicMuteHotkey = "Ctrl+Alt+K", MuteAllHotkey = "ctrl+alt+k" }.Validate());
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"MuteAllHotkey\": \"Ctrl+Alt+K\", \"MicMuteHotkey\": \"alt+ctrl+k\", \"MicrophoneInMixer\": true }");
        var loaded = new SettingsStore(path).Load();
        Assert.Null(loaded.MicMuteHotkey);          // taken by the first one
        Assert.True(loaded.MicrophoneInMixer);
    }
}
