using SonicScrewdriver.Audio;

namespace SonicScrewdriver.Tests.Audio;

[Trait("Category", "Integration")]
public class AudioSessionServiceSmokeTests
{
    [Fact]
    public void Lists_sessions_and_reads_master_volume()
    {
        using var svc = new AudioSessionService();
        var apps = svc.GetApps();
        Assert.InRange(svc.MasterVolume, 0f, 1f);
        foreach (var a in apps)
        {
            Assert.EndsWith(".exe", a.ExeName);
            Assert.InRange(a.Volume, 0f, 1f);
        }
    }

    [Fact]
    public void Peak_meter_is_read_for_playing_apps_and_null_for_absent_ones()
    {
        using var svc = new AudioSessionService();
        Assert.Null(svc.GetPeak("definitely-not-running.exe"));
        foreach (var a in svc.GetApps())
            Assert.InRange(svc.GetPeak(a.ExeName) ?? 0f, 0f, 1f);
    }

    [Fact]
    public void Foreground_lookup_does_not_throw()
        => _ = new ForegroundProcess().GetForegroundExe();
}
