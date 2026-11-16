using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

public class MuteAllTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public MuteAllTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static FakeAudio Audio()
    {
        var a = new FakeAudio();
        a.Apps["a.exe"] = (0.7f, false);
        a.Apps["b.exe"] = (0.4f, true);   // already muted by the user
        a.Apps["c.exe"] = (0.1f, false);
        return a;
    }

    [Fact]
    public void Mutes_everything_then_restores_each_previous_state_exactly()
    {
        var audio = Audio();
        var m = new MuteAll(audio);
        Assert.True(m.Toggle());
        Assert.True(m.Active);
        Assert.All(audio.Apps.Values, v => Assert.True(v.Muted));
        Assert.False(m.Toggle());
        Assert.False(m.Active);
        Assert.False(audio.Apps["a.exe"].Muted);
        Assert.True(audio.Apps["b.exe"].Muted);   // stays muted: it was before
        Assert.False(audio.Apps["c.exe"].Muted);
        Assert.Equal(0.7f, audio.Apps["a.exe"].Vol); // levels untouched
        Assert.Equal(0.4f, audio.Apps["b.exe"].Vol);
        Assert.Empty(audio.SetCalls);
    }

    [Fact]
    public void Master_is_untouched()
    {
        var audio = Audio();
        var m = new MuteAll(audio);
        m.Toggle();
        Assert.False(audio.MasterMuted);
        Assert.Equal(0.5f, audio.MasterVolume);
    }

    [Fact]
    public void Excluded_apps_are_left_alone()
    {
        var audio = Audio();
        var m = new MuteAll(audio) { Excluded = new[] { "A.EXE" } };
        m.Toggle();
        Assert.False(audio.Apps["a.exe"].Muted);
        Assert.True(audio.Apps["c.exe"].Muted);
    }

    [Fact]
    public void Nothing_playing_returns_null_and_stays_inactive()
    {
        var m = new MuteAll(new FakeAudio());
        Assert.Null(m.Toggle());
        Assert.False(m.Active);
    }

    [Fact]
    public void An_app_that_left_is_skipped_and_one_that_started_is_left_alone()
    {
        var audio = Audio();
        var m = new MuteAll(audio);
        m.Toggle();
        audio.Apps.Remove("a.exe");
        audio.Apps["new.exe"] = (1f, false);
        m.Toggle();
        Assert.False(audio.Apps["new.exe"].Muted);
        Assert.False(audio.Apps["c.exe"].Muted);
    }

    [Fact]
    public void Restore_when_inactive_does_nothing()
    {
        var audio = Audio();
        new MuteAll(audio).Restore();
        Assert.False(audio.Apps["a.exe"].Muted);
    }

    [Fact]
    public void Shortcut_settings_are_validated_and_sanitized()
    {
        Assert.Empty(new AppSettings { MuteAllHotkey = "Ctrl+Alt+M" }.Validate());
        Assert.NotEmpty(new AppSettings { MuteAllHotkey = "M" }.Validate());
        Assert.NotEmpty(new AppSettings { MuteAllHotkey = "Ctrl+Alt+M", CompetitiveHotkey = "ctrl+alt+m" }.Validate());

        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"CompetitiveHotkey\": \"Ctrl+Alt+F9\", \"MuteAllHotkey\": \"ctrl+alt+f9\" }");
        Assert.Null(new SettingsStore(path).Load().MuteAllHotkey);
        File.WriteAllText(path, "{ \"MuteAllHotkey\": \"alt+ctrl+m\" }");
        Assert.Equal("Ctrl+Alt+M", new SettingsStore(path).Load().MuteAllHotkey);
        File.WriteAllText(path, "{ \"MuteAllHotkey\": \"nonsense\" }");
        Assert.Null(new SettingsStore(path).Load().MuteAllHotkey);
    }
}
