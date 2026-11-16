using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

public class VolumeLimitsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public VolumeLimitsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Clamp_applies_only_to_listed_apps_case_insensitively()
    {
        var l = new VolumeLimits();
        l.Set(new[] { new AppLimit("Chrome", 40) });
        Assert.Equal(0.4f, l.Clamp("chrome.exe", 0.9f));
        Assert.Equal(0.2f, l.Clamp("CHROME.EXE", 0.2f));
        Assert.Equal(0.9f, l.Clamp("spotify.exe", 0.9f));
        Assert.Null(l.CapOf("spotify.exe"));
    }

    [Fact]
    public void Set_ignores_invalid_entries_and_replaces_everything()
    {
        var l = new VolumeLimits();
        l.Set(new[] { new AppLimit("a", 0), new AppLimit("", 50), new AppLimit("b", 101), new AppLimit("c", 100) });
        Assert.Equal(new[] { "c.exe" }, l.Exes);
        l.Set(null);
        Assert.False(l.Any);
    }

    [Fact]
    public void Shortcut_cannot_go_above_the_cap_and_never_raises_a_lower_volume()
    {
        var audio = new FakeAudio();
        audio.Apps["chrome.exe"] = (0.38f, false);
        audio.Limits.Set(new[] { new AppLimit("chrome.exe", 40) });
        var d = new SonicScrewdriver.Actions.ActionDispatcher(audio, new FakeMedia(), new FakeForeground(), new FakeOsd());
        d.ExecuteAsync(new KeyBinding(Modifiers.Ctrl, Target.Process("chrome.exe"), 10), MediaKey.VolumeUp).Wait();
        Assert.Equal(0.4f, audio.Apps["chrome.exe"].Vol);
    }

    [Fact]
    public void Enforcer_brings_back_an_app_that_went_above_its_cap()
    {
        var audio = new FakeAudio();
        audio.Apps["chrome.exe"] = (0.9f, false);
        audio.Apps["spotify.exe"] = (0.9f, false);
        audio.Limits.Set(new[] { new AppLimit("chrome.exe", 40) });
        var e = new LimitEnforcer(audio);
        Assert.Equal(1, e.Enforce());
        Assert.Equal(0.4f, audio.Apps["chrome.exe"].Vol);
        Assert.Equal(0.9f, audio.Apps["spotify.exe"].Vol);
        Assert.Equal(0, e.Enforce()); // nothing left to fix: no write, no loop
    }

    [Fact]
    public void Enforcer_ignores_an_absent_app_and_does_not_touch_an_app_at_its_cap()
    {
        var audio = new FakeAudio();
        audio.Apps["a.exe"] = (0.4f, false);
        audio.Limits.Set(new[] { new AppLimit("a.exe", 40), new AppLimit("gone.exe", 10) });
        Assert.Equal(0, new LimitEnforcer(audio).Enforce());
        Assert.Empty(audio.SetCalls);
    }

    [Fact]
    public void Settings_validate_and_sanitize_the_limits()
    {
        Assert.Empty(new AppSettings { MaxVolumes = { new AppLimit("chrome.exe", 40) } }.Validate());
        Assert.NotEmpty(new AppSettings { MaxVolumes = { new AppLimit("chrome.exe", 0) } }.Validate());
        Assert.NotEmpty(new AppSettings { MaxVolumes = { new AppLimit("chrome.exe", 101) } }.Validate());
        Assert.NotEmpty(new AppSettings { MaxVolumes = { new AppLimit("", 50) } }.Validate());
        Assert.NotEmpty(new AppSettings { MaxVolumes = { new AppLimit("Chrome", 50), new AppLimit("chrome.exe", 60) } }.Validate());

        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"MaxVolumes\": [ {\"ExeName\":\" Chrome \",\"Percent\":500}, {\"ExeName\":\"chrome.exe\",\"Percent\":10}, {\"ExeName\":\"\",\"Percent\":5}, {\"ExeName\":\"b\",\"Percent\":-3}, null ] }");
        var loaded = new SettingsStore(path).Load().MaxVolumes;
        Assert.Equal(new[] { new AppLimit("chrome.exe", 100), new AppLimit("b.exe", 1) }, loaded);
    }
}
