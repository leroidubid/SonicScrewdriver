using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

/// <summary>Nothing may change a volume or a route once competitive mode is on, even for a callback that was already queued.</summary>
public class CompetitiveGuardTests
{
    private static FakeAudio Audio()
    {
        var a = new FakeAudio();
        a.Apps["discord.exe"] = (0.9f, false);
        a.Apps["spotify.exe"] = (0.8f, false);
        return a;
    }

    [Fact]
    public void Ducking_tick_in_flight_when_competitive_turns_on_restores_and_does_nothing()
    {
        var a = Audio();
        var cfg = new DuckingSettings(true, 30, 4, 0, 200, new() { new("discord.exe", true, false), new("spotify.exe", false, true) });
        var competitive = false;
        var e = new DuckingEngine(a, () => cfg, blocked: () => competitive);
        a.Peaks["discord.exe"] = 0.5f;
        e.Tick(0);
        Assert.Equal(0.8f * 0.3f, a.Apps["spotify.exe"].Vol, 3);
        competitive = true;
        e.Tick(100);                                   // a tick already queued before the switch
        Assert.False(e.Active);
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol); // exact previous level back
        a.SetCalls.Clear();
        e.Tick(200); e.Tick(300);
        Assert.Empty(a.SetCalls);                      // and nothing lowered again
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol);
    }

    [Fact]
    public void Streamer_tick_does_nothing_in_competitive_mode()
    {
        var a = Audio();
        var cfg = new StreamerSettings(true, 30, 5, 0, 200, new() { new StreamApp("spotify.exe", true) });
        var e = new DuckingEngine(a, () => cfg.ToDucking(), peakSource: () => 0.5f, blocked: () => true);
        e.Tick(0); e.Tick(100);
        Assert.Empty(a.SetCalls);
        Assert.False(e.Active);
    }

    [Fact]
    public void Limit_enforcer_does_nothing_in_competitive_mode()
    {
        var a = Audio();
        a.Limits.Set(new[] { new AppLimit("spotify.exe", 50) });
        var competitive = true;
        var enforcer = new LimitEnforcer(a, () => competitive);
        Assert.Equal(0, enforcer.Enforce());
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol);
        competitive = false;
        Assert.Equal(1, enforcer.Enforce());           // the observer works again afterwards
        Assert.Equal(0.5f, a.Apps["spotify.exe"].Vol, 3);
    }

    private sealed class Router : IAppRouter
    {
        public int Sets;
        public string? GetDevice(int pid) => null;
        public bool SetDevice(int pid, string? device) { Sets++; return true; }
    }

    [Fact]
    public void Routing_does_nothing_in_competitive_mode_and_resumes_after()
    {
        var r = new Router();
        var competitive = true;
        var applier = new RoutingApplier(r, () => competitive);
        var rules = new[] { new RoutingRule("discord.exe", "headset") };
        Assert.Equal(0, applier.Apply(rules, new[] { "headset" }, _ => new[] { 10 }));
        Assert.Equal(0, r.Sets);
        competitive = false;
        Assert.Equal(1, applier.Apply(rules, new[] { "headset" }, _ => new[] { 10 }));
    }

    [Fact]
    public void Device_priority_does_not_switch_the_default_in_competitive_mode()
    {
        var a = Audio();
        a.DeviceList = new()
        {
            new("speakers", "Speakers", true, 1, false, Array.Empty<AudioApp>()),
            new("headset", "Headset", false, 1, false, Array.Empty<AudioApp>()),
        };
        var policy = new DevicePriorityPolicy();
        var order = new[] { "headset", "speakers" };
        Assert.Null(policy.Apply(a, order, () => true));
        Assert.Empty(a.DefaultCalls);
        Assert.Equal("headset", policy.Apply(a, order, () => false)); // the blocked call did not consume the evaluation
        Assert.Equal(new[] { "headset" }, a.DefaultCalls);
    }

    [Fact]
    public void Profile_retry_queued_before_competitive_mode_applies_nothing()
    {
        var a = Audio();
        var retry = new ProfileRetry();
        retry.Add(new VolumeProfile("p", new() { new ProfileApp("spotify.exe", 10) }, null));
        retry.Run(a, () => true);
        Assert.Equal(0.8f, a.Apps["spotify.exe"].Vol);
        Assert.False(retry.Pending);
        retry.Add(new VolumeProfile("p", new() { new ProfileApp("spotify.exe", 10) }, null));
        retry.Run(a, () => false);
        Assert.Equal(0.1f, a.Apps["spotify.exe"].Vol, 3);
    }
}
