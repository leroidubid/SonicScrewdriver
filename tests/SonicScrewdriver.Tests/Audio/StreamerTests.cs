using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

public class StreamerTests
{
    private static StreamerSettings Cfg(int to = 30, int attack = 100, int release = 1000, bool spotify = true, bool chrome = true) =>
        new(true, to, 5, attack, release, new() { new("spotify.exe", spotify), new("chrome.exe", chrome), new("game.exe", false) });

    private sealed class Rig
    {
        public readonly FakeAudio Audio = new();
        public float? Mic = 0f;
        public StreamerSettings Streamer;
        public DuckingSettings Ducking = new(true, 50, 4, 0, 200, new() { new("discord.exe", true, false), new("spotify.exe", false, true) });
        public readonly DuckingEngine Stream, Duck;

        public Rig(StreamerSettings? cfg = null)
        {
            Streamer = cfg ?? Cfg();
            Audio.Apps["discord.exe"] = (0.9f, false);
            Audio.Apps["spotify.exe"] = (0.8f, false);
            Audio.Apps["chrome.exe"] = (0.5f, false);
            Audio.Apps["game.exe"] = (1f, false);
            var ledger = new DuckLedger(Audio);
            Stream = new DuckingEngine(Audio, () => Streamer.ToDucking(), ledger, () => Mic);
            Duck = new DuckingEngine(Audio, () => Ducking, ledger);
        }

        public float Vol(string exe) => Audio.Apps[exe].Vol;
    }

    [Fact]
    public void Speech_must_last_the_attack_delay_and_the_threshold_is_inclusive()
    {
        var r = new Rig();
        r.Mic = 0.049f;                                           // room noise: threshold is 5 %
        r.Stream.Tick(0); r.Stream.Tick(500);
        Assert.False(r.Stream.Active);
        r.Mic = 0.05f;
        r.Stream.Tick(600); r.Stream.Tick(650);
        Assert.False(r.Stream.Active);
        r.Stream.Tick(700);
        Assert.True(r.Stream.Active);
        Assert.Equal(0.8f * 0.3f, r.Vol("spotify.exe"), 3);
        Assert.Equal(0.5f * 0.3f, r.Vol("chrome.exe"), 3);
    }

    [Fact]
    public void Only_the_ticked_apps_are_lowered()
    {
        var r = new Rig(Cfg(chrome: false));
        r.Mic = 0.5f;
        r.Stream.Tick(0); r.Stream.Tick(100);
        Assert.Equal(0.8f * 0.3f, r.Vol("spotify.exe"), 3);
        Assert.Equal(0.5f, r.Vol("chrome.exe"));                  // listed but not ticked
        Assert.Equal(1f, r.Vol("game.exe"));
        Assert.Equal(0.9f, r.Vol("discord.exe"));                 // not listed
    }

    [Fact]
    public void Levels_come_back_exactly_after_the_release_delay_and_pauses_do_not_pump()
    {
        var r = new Rig();
        r.Mic = 0.3f;
        r.Stream.Tick(0); r.Stream.Tick(100);
        r.Mic = 0f;
        r.Stream.Tick(200); r.Stream.Tick(1100);
        Assert.True(r.Stream.Active);                             // 900 ms of silence: still lowered
        r.Mic = 0.3f; r.Stream.Tick(1150);                        // a new word: the silence restarts
        r.Mic = 0f;
        r.Stream.Tick(1200); r.Stream.Tick(2100);
        Assert.True(r.Stream.Active);
        r.Stream.Tick(2200);
        Assert.False(r.Stream.Active);
        Assert.Equal(0.8f, r.Vol("spotify.exe"));
        Assert.Equal(0.5f, r.Vol("chrome.exe"));
    }

    [Fact]
    public void A_slider_moved_while_lowered_is_respected()
    {
        var r = new Rig();
        r.Mic = 0.3f;
        r.Stream.Tick(0); r.Stream.Tick(100);
        r.Audio.Apps["spotify.exe"] = (0.6f, false);
        r.Stream.Tick(150);
        r.Mic = 0f;
        r.Stream.Tick(200); r.Stream.Tick(1300);
        Assert.Equal(0.6f, r.Vol("spotify.exe"));
        Assert.Equal(0.5f, r.Vol("chrome.exe"));
    }

    [Fact]
    public void A_missing_microphone_reads_as_silence_and_restores()
    {
        var r = new Rig();
        r.Mic = 0.3f;
        r.Stream.Tick(0); r.Stream.Tick(100);
        r.Mic = null;                                             // unplugged
        r.Stream.Tick(200); r.Stream.Tick(1300);
        Assert.False(r.Stream.Active);
        Assert.Equal(0.8f, r.Vol("spotify.exe"));
    }

    [Fact]
    public void Restore_puts_everything_back_at_once_and_is_idempotent()
    {
        var r = new Rig();
        r.Mic = 0.3f;
        r.Stream.Tick(0); r.Stream.Tick(100);
        r.Stream.Restore();
        r.Stream.Restore();
        Assert.False(r.Stream.Active);
        Assert.Equal(0.8f, r.Vol("spotify.exe"));
        Assert.Equal(0.5f, r.Vol("chrome.exe"));
    }

    [Fact]
    public void An_app_that_starts_while_lowered_is_lowered_too()
    {
        var r = new Rig();
        r.Audio.Apps.Remove("chrome.exe");
        r.Mic = 0.3f;
        r.Stream.Tick(0); r.Stream.Tick(100);
        r.Audio.Apps["chrome.exe"] = (0.7f, false);
        r.Stream.Tick(200);
        Assert.Equal(0.7f * 0.3f, r.Vol("chrome.exe"), 3);
        r.Stream.Restore();
        Assert.Equal(0.7f, r.Vol("chrome.exe"), 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_modes_at_once_never_double_duck_and_levels_always_come_back(bool duckingStopsFirst)
    {
        var r = new Rig();
        r.Audio.Apps["discord.exe"] = (0.9f, false);
        r.Audio.Peaks["discord.exe"] = 0.3f;                      // a voice app speaks: ducking lowers Spotify to 50 %
        r.Duck.Tick(0);
        Assert.Equal(0.4f, r.Vol("spotify.exe"), 3);
        r.Mic = 0.3f;                                             // and the user speaks too: streamer wants 30 %
        r.Stream.Tick(10); r.Stream.Tick(110);
        Assert.Equal(0.8f * 0.3f, r.Vol("spotify.exe"), 3);       // original x lowest factor, not 0.8 x 0.5 x 0.3
        Assert.Equal(0.5f * 0.3f, r.Vol("chrome.exe"), 3);

        if (duckingStopsFirst)
        {
            r.Audio.Peaks["discord.exe"] = 0f;
            r.Duck.Tick(200); r.Duck.Tick(500);
            Assert.False(r.Duck.Active);
            Assert.Equal(0.8f * 0.3f, r.Vol("spotify.exe"), 3);   // the streamer still holds it
            r.Stream.Restore();
        }
        else
        {
            r.Stream.Restore();
            Assert.Equal(0.8f * 0.5f, r.Vol("spotify.exe"), 3);   // back to the ducking level, not to the original yet
            r.Audio.Peaks["discord.exe"] = 0f;
            r.Duck.Tick(200); r.Duck.Tick(500);
            Assert.False(r.Duck.Active);
        }
        Assert.Equal(0.8f, r.Vol("spotify.exe"), 3);
        Assert.Equal(0.5f, r.Vol("chrome.exe"), 3);
        Assert.Equal(0.9f, r.Vol("discord.exe"));
    }

    [Fact]
    public void A_slider_moved_while_both_are_active_is_left_alone_by_both()
    {
        var r = new Rig();
        r.Audio.Peaks["discord.exe"] = 0.3f;
        r.Duck.Tick(0);
        r.Mic = 0.3f;
        r.Stream.Tick(10); r.Stream.Tick(110);
        r.Audio.Apps["spotify.exe"] = (0.7f, false);              // the user drags it
        r.Stream.Tick(150);
        r.Duck.Tick(160);
        Assert.Equal(0.7f, r.Vol("spotify.exe"));                 // nobody pulls it back down
        r.Audio.Peaks["discord.exe"] = 0f; r.Mic = 0f;
        r.Duck.Tick(200); r.Duck.Tick(500); r.Stream.Tick(200); r.Stream.Tick(1300);
        Assert.Equal(0.7f, r.Vol("spotify.exe"));                 // and nobody restores over it
        Assert.Equal(0.5f, r.Vol("chrome.exe"));
    }

    [Fact]
    public void ToDucking_keeps_only_ticked_apps_and_no_voice_app()
    {
        var d = Cfg(chrome: false).ToDucking();
        Assert.Equal(new[] { "spotify.exe" }, d.AppList.Select(a => a.ExeName));
        Assert.All(d.AppList, a => { Assert.False(a.Voice); Assert.True(a.Ducked); });
        Assert.Equal(5, d.ThresholdPercent);
    }

    // ---- settings ----

    [Fact]
    public void Defaults_are_off_with_a_5_percent_threshold_and_30_percent_level()
    {
        var s = new StreamerSettings();
        Assert.False(s.Enabled);
        Assert.False(s.Active);
        Assert.Equal(30, s.DuckToPercent);
        Assert.Equal(5, s.ThresholdPercent);
        Assert.Empty(s.Validate());                               // off with an empty list is valid
        Assert.False(new AppSettings().Streamer.Enabled);
    }

    [Fact]
    public void Active_needs_the_switch_and_a_ticked_app()
    {
        Assert.True(Cfg().Active);
        Assert.False((Cfg() with { Enabled = false }).Active);
        Assert.False(Cfg(spotify: false, chrome: false).Active);
    }

    [Fact]
    public void Enabled_requires_a_ticked_app_and_valid_numbers()
    {
        Assert.Contains(StreamerSettings.Sanitize(Cfg(spotify: false, chrome: false)).Validate(), e => e.Contains("tick at least one app"));
        var bad = new StreamerSettings(true, 101, 0, -1, 50, new() { new("", true), new("a.exe", true), new("A.EXE", true) });
        var errors = bad.Validate();
        Assert.Equal(6, errors.Count);                            // four numbers, an empty row and a duplicate
        Assert.Contains(errors, e => e.Contains("already in the list"));
        Assert.Empty(Cfg().Validate());
        Assert.NotEmpty(new AppSettings { Streamer = bad }.Validate());
    }

    [Fact]
    public void Sanitize_clamps_numbers_normalizes_names_and_drops_duplicates()
    {
        var s = StreamerSettings.Sanitize(new StreamerSettings(true, 500, 0, 99999, 1, new()
        {
            new(" Spotify.EXE ", true), new("spotify.exe", false), new("", true), new("chrome", false),
        }));
        Assert.Equal(100, s.DuckToPercent);
        Assert.Equal(StreamerSettings.MinThreshold, s.ThresholdPercent);
        Assert.Equal(StreamerSettings.MaxAttackMs, s.AttackMs);
        Assert.Equal(StreamerSettings.MinReleaseMs, s.ReleaseMs);
        Assert.Equal(new[] { "spotify.exe", "chrome.exe" }, s.AppList.Select(a => a.ExeName));
        Assert.True(s.AppList[0].Lowered);                        // the first duplicate wins
        Assert.False(s.AppList[1].Lowered);                       // an unticked row is kept
        Assert.Empty(StreamerSettings.Sanitize(null).AppList);
    }
}
