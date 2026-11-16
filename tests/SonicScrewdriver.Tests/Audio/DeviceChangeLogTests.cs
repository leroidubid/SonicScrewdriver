using SonicScrewdriver.Audio;

namespace SonicScrewdriver.Tests.Audio;

/// <summary>Second review, finding 9: a device notification that lands while the trackers are rebuilt must not be lost.</summary>
public class DeviceChangeLogTests
{
    [Fact]
    public void A_notification_during_the_rebuild_keeps_the_log_dirty_and_its_device_for_the_next_rebuild()
    {
        var log = new DeviceChangeLog();
        log.Mark("usb");
        var first = log.Begin(afterTake: () => log.Mark("headset"));   // replug while the rebuild is running
        Assert.Contains("usb", first);
        Assert.DoesNotContain("headset", first);
        Assert.True(log.Dirty);                                          // the old order cleared the flag after this and lost it
        Assert.Contains("headset", log.Begin());                         // the next rebuild replaces that device's tracker
        Assert.False(log.Dirty);
    }

    [Fact]
    public void A_quiet_rebuild_clears_the_flag()
    {
        var log = new DeviceChangeLog();
        log.Mark("usb");
        Assert.Single(log.Begin());
        Assert.False(log.Dirty);
        Assert.Empty(log.Begin());
    }

    [Fact]
    public void A_notification_without_id_only_marks_dirty()
    {
        var log = new DeviceChangeLog();
        log.Begin();
        log.Mark(null);
        Assert.True(log.Dirty);
        Assert.Empty(log.Begin());
    }
}
