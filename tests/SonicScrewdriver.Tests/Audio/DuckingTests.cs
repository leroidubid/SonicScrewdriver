using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

public class DuckingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public DuckingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static DuckingSettings Cfg(int to = 30, int attack = 100, int release = 1000) => new(true, to, 4, attack, release,
        new() { new("discord.exe", true, false), new("spotify.exe", false, true), new("chrome.exe", false, true) });

    private static (DuckingEngine Engine, FakeAudio Audio, DuckingSettings Cfg) Make(DuckingSettings? cfg = null)
    {
        cfg ??= Cfg();
        var audio = new FakeAudio();
        audio.Apps["discord.exe"] = (0.9f, false);
        audio.Apps["spotify.exe"] = (0.8f, false);
        audio.Apps["chrome.exe"] = (0.5f, false);
        var engine = new DuckingEngine(audio, () => cfg);
        return (engine, audio, cfg);
    }

    [Fact]
    public void Voice_must_last_the_attack_delay_before_apps_are_lowered()
    {
        var (e, a, _) = Make();
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0);
        e.Tick(50);
        Assert.False(e.Active);
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol);
        e.Tick(100);
        Assert.True(e.Active);
        Assert.Equal(0.8f * 0.3f, a.Apps["spotify.exe"].Vol, 3);
        Assert.Equal(0.5f * 0.3f, a.Apps["chrome.exe"].Vol, 3);
        Assert.Equal(0.9f, a.Apps["discord.exe"].Vol);          // the voice app itself is never lowered
    }

    [Fact]
    public void A_blip_shorter_than_the_attack_delay_does_nothing()
    {
        var (e, a, _) = Make();
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0);
        a.Peaks["discord.exe"] = 0f;
        e.Tick(60);
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(120);                                             // the loud period restarted at 120
        e.Tick(180);
        Assert.False(e.Active);
        e.Tick(220);
        Assert.True(e.Active);
    }

    [Fact]
    public void Levels_return_exactly_only_after_the_release_delay()
    {
        var (e, a, _) = Make();
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0); e.Tick(100);
        a.Peaks["discord.exe"] = 0f;
        e.Tick(200);                                             // quiet since 200
        e.Tick(1100);
        Assert.True(e.Active);                                   // 900 ms of quiet: not yet
        a.Peaks["discord.exe"] = 0.3f; e.Tick(1150);             // a word in between: the quiet period restarts
        a.Peaks["discord.exe"] = 0f;
        e.Tick(1200); e.Tick(2100);
        Assert.True(e.Active);
        e.Tick(2200);
        Assert.False(e.Active);
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol);
        Assert.Equal(0.5f, a.Apps["chrome.exe"].Vol);
    }

    [Fact]
    public void A_slider_moved_while_lowered_is_respected_and_not_restored_over()
    {
        var (e, a, _) = Make();
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0); e.Tick(100);
        a.Apps["spotify.exe"] = (0.6f, false);                   // the user drags Spotify up while it is lowered
        e.Tick(150);
        Assert.DoesNotContain("spotify.exe", e.Originals.Keys);
        a.Peaks["discord.exe"] = 0f;
        e.Tick(200); e.Tick(1300);
        Assert.False(e.Active);
        Assert.Equal(0.6f, a.Apps["spotify.exe"].Vol);           // left as the user set it
        Assert.Equal(0.5f, a.Apps["chrome.exe"].Vol);            // the other one is back
        // Next episode starts from the new level.
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(2000); e.Tick(2100);
        Assert.Equal(0.6f * 0.3f, a.Apps["spotify.exe"].Vol, 3);
    }

    [Fact]
    public void An_app_that_starts_while_ducking_is_lowered_too_and_one_that_left_is_skipped()
    {
        var (e, a, _) = Make();
        a.Apps.Remove("chrome.exe");
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0); e.Tick(100);
        a.Apps["chrome.exe"] = (0.7f, false);
        e.Tick(200);
        Assert.Equal(0.7f * 0.3f, a.Apps["chrome.exe"].Vol, 3);
        a.Apps.Remove("spotify.exe");
        a.Peaks["discord.exe"] = 0f;
        e.Tick(300); e.Tick(1400);
        Assert.Equal(0.7f, a.Apps["chrome.exe"].Vol, 3);
    }

    [Fact]
    public void Peak_below_the_threshold_is_silence()
    {
        var (e, a, _) = Make();
        a.Peaks["discord.exe"] = 0.039f;                         // threshold is 4 %
        e.Tick(0); e.Tick(500);
        Assert.False(e.Active);
        a.Peaks["discord.exe"] = 0.04f;
        e.Tick(600); e.Tick(700);
        Assert.True(e.Active);
    }

    [Fact]
    public void Restore_puts_everything_back_at_once_and_is_idempotent()
    {
        var (e, a, _) = Make();
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0); e.Tick(100);
        e.Restore();
        Assert.False(e.Active);
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol);
        Assert.Equal(0.5f, a.Apps["chrome.exe"].Vol);
        a.SetCalls.Clear();
        e.Restore();
        Assert.Empty(a.SetCalls);
    }

    [Fact]
    public void Voice_app_without_a_session_and_apps_not_marked_are_ignored()
    {
        var (e, a, _) = Make();
        a.Apps["game.exe"] = (0.9f, false);
        a.Apps.Remove("discord.exe");                            // no voice session: no peak
        e.Tick(0); e.Tick(1000);
        Assert.False(e.Active);
        a.Apps["discord.exe"] = (0.9f, false);
        a.Peaks["discord.exe"] = 0.5f;
        e.Tick(2000); e.Tick(2200);
        Assert.Equal(0.9f, a.Apps["game.exe"].Vol);              // neither voice nor lowered
    }

    [Fact]
    public void Works_together_with_the_maximum_volume_cap()
    {
        var (e, a, _) = Make(Cfg(to: 50));
        a.Limits.Set(new[] { new AppLimit("spotify.exe", 40) });
        a.Apps["spotify.exe"] = (0.4f, false);
        a.Peaks["discord.exe"] = 0.3f;
        e.Tick(0); e.Tick(100);
        Assert.Equal(0.2f, a.Apps["spotify.exe"].Vol, 3);
        a.Peaks["discord.exe"] = 0f;
        e.Tick(200); e.Tick(1300);
        Assert.Equal(0.4f, a.Apps["spotify.exe"].Vol, 3);
    }

    [Fact]
    public void Settings_are_off_by_default_validated_and_sanitized()
    {
        Assert.False(new AppSettings().Ducking.Enabled);
        Assert.Empty(new AppSettings().Validate());
        Assert.Empty(new AppSettings { Ducking = Cfg() }.Validate());
        Assert.NotEmpty(new AppSettings { Ducking = Cfg() with { DuckToPercent = 101 } }.Validate());
        Assert.NotEmpty(new AppSettings { Ducking = Cfg() with { ThresholdPercent = 0 } }.Validate());
        Assert.NotEmpty(new AppSettings { Ducking = Cfg() with { AttackMs = 5000 } }.Validate());
        Assert.NotEmpty(new AppSettings { Ducking = Cfg() with { ReleaseMs = 10 } }.Validate());
        Assert.NotEmpty(new AppSettings { Ducking = new DuckingSettings(true, Apps: new() { new("a", false, true) }) }.Validate()); // no voice app
        Assert.NotEmpty(new AppSettings { Ducking = new DuckingSettings(true, Apps: new() { new("a", true, false) }) }.Validate()); // nothing to lower
        Assert.NotEmpty(new AppSettings { Ducking = new DuckingSettings(false, Apps: new() { new("a", true, false), new("A.exe", false, true) }) }.Validate());

        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"Ducking\": { \"Enabled\": true, \"DuckToPercent\": 900, \"ThresholdPercent\": -3, \"AttackMs\": 99999, \"ReleaseMs\": 1, " +
            "\"Apps\": [ {\"ExeName\":\" Discord \",\"Voice\":true,\"Ducked\":false}, {\"ExeName\":\"discord.exe\",\"Voice\":false,\"Ducked\":true}, {\"ExeName\":\"x\",\"Voice\":false,\"Ducked\":false}, {\"ExeName\":\"\",\"Voice\":true,\"Ducked\":true}, null ] } }");
        var d = new SettingsStore(path).Load().Ducking;
        Assert.Equal(100, d.DuckToPercent);
        Assert.Equal(1, d.ThresholdPercent);
        Assert.Equal(2000, d.AttackMs);
        Assert.Equal(200, d.ReleaseMs);
        Assert.Equal(new[] { new DuckApp("discord.exe", true, false) }, d.AppList);

        File.WriteAllText(path, "{ \"Ducking\": null }");
        Assert.False(new SettingsStore(path).Load().Ducking.Enabled);
    }

    [Fact]
    public void Settings_roundtrip_without_computed_properties()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { Ducking = Cfg() });
        Assert.DoesNotContain("AppList", File.ReadAllText(path));
        Assert.Equal(3, store.Load().Ducking.AppList.Count);
    }
}
