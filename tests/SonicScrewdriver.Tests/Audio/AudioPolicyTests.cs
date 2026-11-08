using System.Diagnostics;
using SonicScrewdriver.Audio;

namespace SonicScrewdriver.Tests.Audio;

public class AudioPolicyIdTests
{
    private const string Id = "{0.0.0.00000000}.{6b3c0b4e-7b1f-4a3a-9a3c-1234567890ab}";

    [Fact]
    public void Persisted_id_roundtrips()
        => Assert.Equal(Id, AudioPolicy.MmDeviceIdFromPersisted(AudioPolicy.PersistedId(Id)));

    [Fact]
    public void Foreign_strings_are_rejected()
        => Assert.Null(AudioPolicy.MmDeviceIdFromPersisted("something else"));
}

[Trait("Category", "Integration")]
public class AudioPolicyIntegrationTests
{
    [Fact]
    public void Per_app_device_set_get_and_reset_on_this_process()
    {
        using var svc = new AudioSessionService();
        var device = svc.GetDevices().FirstOrDefault();
        if (device is null) return; // no output device on this machine
        var pid = Environment.ProcessId;
        try
        {
            Assert.True(AudioPolicy.SetAppDevice(pid, device.Id));
            Assert.Equal(device.Id, AudioPolicy.GetAppDevice(pid));
        }
        finally
        {
            Assert.True(AudioPolicy.SetAppDevice(pid, null));
        }
        Assert.Null(AudioPolicy.GetAppDevice(pid));
    }

    [Fact]
    public void Lists_devices_with_default_first()
    {
        using var svc = new AudioSessionService();
        var devices = svc.GetDevices();
        if (devices.Count == 0) return;
        Assert.True(devices[0].IsDefault);
        Assert.All(devices, d => Assert.InRange(d.Volume, 0f, 1f));
    }
}
