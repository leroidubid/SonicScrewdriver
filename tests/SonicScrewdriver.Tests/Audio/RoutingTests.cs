using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Audio;

public class RoutingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public RoutingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeRouter : IAppRouter
    {
        public Dictionary<int, string?> Routes { get; } = new();
        public List<(int Pid, string? Device)> Sets { get; } = new();
        public string? GetDevice(int pid) => Routes.TryGetValue(pid, out var d) ? d : null;
        public bool SetDevice(int pid, string? device) { Sets.Add((pid, device)); Routes[pid] = device; return true; }
    }

    private static readonly RoutingRule[] Rules = { new("discord.exe", "headset"), new("spotify.exe", "speakers") };
    private static readonly string[] Both = { "headset", "speakers" };

    private static IReadOnlyList<int> Pids(string exe) => exe switch
    {
        "discord.exe" => new[] { 10 },
        "spotify.exe" => new[] { 20, 21 },
        _ => Array.Empty<int>(),
    };

    [Fact]
    public void Routes_each_new_process_to_its_device()
    {
        var router = new FakeRouter();
        var a = new RoutingApplier(router);
        Assert.Equal(3, a.Apply(Rules, Both, Pids));
        Assert.Equal("headset", router.Routes[10]);
        Assert.Equal("speakers", router.Routes[20]);
        Assert.Equal("speakers", router.Routes[21]);
    }

    [Fact]
    public void A_process_is_handled_once_so_a_manual_choice_stays()
    {
        var router = new FakeRouter();
        var a = new RoutingApplier(router);
        a.Apply(Rules, Both, Pids);
        router.Routes[10] = "speakers"; // the user moved Discord by hand
        router.Sets.Clear();
        Assert.Equal(0, a.Apply(Rules, Both, Pids));
        Assert.Empty(router.Sets);
        Assert.Equal("speakers", router.Routes[10]);
    }

    [Fact]
    public void A_missing_device_is_skipped_then_applied_when_it_is_plugged_in()
    {
        var router = new FakeRouter();
        var a = new RoutingApplier(router);
        Assert.Equal(2, a.Apply(Rules, new[] { "speakers" }, Pids));
        Assert.False(router.Routes.ContainsKey(10));
        Assert.Equal(1, a.Apply(Rules, Both, Pids));
        Assert.Equal("headset", router.Routes[10]);
    }

    [Fact]
    public void An_already_correct_route_is_not_written()
    {
        var router = new FakeRouter();
        router.Routes[10] = "headset";
        new RoutingApplier(router).Apply(new[] { Rules[0] }, Both, Pids);
        Assert.Empty(router.Sets);
    }

    [Fact]
    public void A_restarted_app_is_routed_again_and_reset_forgets_everything()
    {
        var router = new FakeRouter();
        var a = new RoutingApplier(router);
        a.Apply(Rules, Both, Pids);
        router.Sets.Clear();
        a.Apply(Rules, Both, exe => Array.Empty<int>()); // everything exited
        router.Routes.Clear();
        a.Apply(Rules, Both, Pids);                       // same pids again (reused): handled afresh
        Assert.Equal(3, router.Sets.Count);
        router.Sets.Clear();
        a.Reset();
        router.Routes.Clear();
        a.Apply(Rules, Both, Pids);
        Assert.Equal(3, router.Sets.Count);
    }

    [Fact]
    public void Settings_are_validated_and_sanitized()
    {
        Assert.Empty(new AppSettings { RoutingRules = { new("discord", "id1", "Headset") } }.Validate());
        Assert.NotEmpty(new AppSettings { RoutingRules = { new("", "id1") } }.Validate());
        Assert.NotEmpty(new AppSettings { RoutingRules = { new("a", "") } }.Validate());
        Assert.NotEmpty(new AppSettings { RoutingRules = { new("a", "x"), new("A.exe", "y") } }.Validate());

        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"RoutingRules\": [ {\"ExeName\":\" Discord \",\"DeviceId\":\" id1 \",\"DeviceName\":null}, {\"ExeName\":\"discord.exe\",\"DeviceId\":\"id2\"}, {\"ExeName\":\"x\",\"DeviceId\":\"\"}, null ] }");
        Assert.Equal(new[] { new RoutingRule("discord.exe", "id1", "") }, new SettingsStore(path).Load().RoutingRules);
    }
}
