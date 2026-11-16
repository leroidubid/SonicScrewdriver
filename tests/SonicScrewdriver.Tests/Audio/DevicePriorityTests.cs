using System.IO;
using SonicScrewdriver.Audio;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Audio;

public class DevicePriorityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mixkeys-tests-" + Guid.NewGuid());
    public DevicePriorityTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly string[] Order = { "headset", "speakers", "monitor" };

    [Fact]
    public void Top_is_the_first_plugged_in_device_of_the_list()
    {
        Assert.Equal("headset", DevicePriorityPolicy.Top(Order, new[] { "monitor", "headset" }));
        Assert.Equal("speakers", DevicePriorityPolicy.Top(Order, new[] { "monitor", "speakers", "other" }));
        Assert.Null(DevicePriorityPolicy.Top(Order, new[] { "other" }));
        Assert.Null(DevicePriorityPolicy.Top(Array.Empty<string>(), new[] { "x" }));
    }

    [Fact]
    public void First_evaluation_switches_to_the_top_choice_unless_it_is_already_the_default()
    {
        Assert.Equal("headset", new DevicePriorityPolicy().Evaluate(Order, new[] { "headset", "speakers" }, "speakers"));
        Assert.Null(new DevicePriorityPolicy().Evaluate(Order, new[] { "headset", "speakers" }, "headset"));
    }

    [Fact]
    public void A_manual_choice_is_kept_until_the_top_choice_changes()
    {
        var p = new DevicePriorityPolicy();
        Assert.Equal("headset", p.Evaluate(Order, new[] { "headset", "speakers" }, "speakers"));
        // The user picks the speakers by hand: the top choice is still the headset.
        Assert.Null(p.Evaluate(Order, new[] { "headset", "speakers" }, "speakers"));
        // An unrelated device appears: still no change of the top.
        Assert.Null(p.Evaluate(Order, new[] { "headset", "speakers", "other" }, "speakers"));
        // The headset is unplugged: the top becomes the speakers (already the default: nothing to do).
        Assert.Null(p.Evaluate(Order, new[] { "speakers" }, "speakers"));
        // The headset is plugged in again: the top changed, so it becomes the default.
        Assert.Equal("headset", p.Evaluate(Order, new[] { "headset", "speakers" }, "speakers"));
    }

    [Fact]
    public void Unplugging_the_default_moves_to_the_next_device_of_the_list()
    {
        var p = new DevicePriorityPolicy();
        p.Evaluate(Order, new[] { "headset", "speakers" }, "headset");
        Assert.Equal("speakers", p.Evaluate(Order, new[] { "speakers", "monitor" }, "monitor")); // Windows fell back to the monitor
    }

    [Fact]
    public void No_device_of_the_list_plugged_in_does_nothing_and_Reset_makes_it_act_again()
    {
        var p = new DevicePriorityPolicy();
        Assert.Null(p.Evaluate(Order, new[] { "other" }, "other"));
        Assert.Equal("monitor", p.Evaluate(Order, new[] { "monitor", "other" }, "other"));
        Assert.Null(p.Evaluate(Order, new[] { "monitor", "other" }, "other"));
        p.Reset();
        Assert.Equal("monitor", p.Evaluate(Order, new[] { "monitor", "other" }, "other"));
    }

    [Fact]
    public void Settings_validate_and_sanitize_the_list()
    {
        Assert.Empty(new AppSettings().Validate());
        Assert.False(new AppSettings().DevicePriorityEnabled);
        Assert.Empty(new AppSettings { DevicePriorityEnabled = true, DevicePriority = { new("a", "A") } }.Validate());
        Assert.NotEmpty(new AppSettings { DevicePriorityEnabled = true }.Validate());
        Assert.NotEmpty(new AppSettings { DevicePriority = { new("a"), new("a") } }.Validate());

        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"DevicePriorityEnabled\": true, \"DevicePriority\": [ {\"Id\":\" b \",\"Name\":\"B\"}, {\"Id\":\"a\"}, {\"Id\":\"b\"}, {\"Id\":\"\"}, null ] }");
        var s = new SettingsStore(path).Load();
        Assert.True(s.DevicePriorityEnabled);
        Assert.Equal(new[] { new DeviceRef("b", "B"), new DeviceRef("a", "") }, s.DevicePriority);
    }
}
