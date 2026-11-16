using SonicScrewdriver.Audio;

namespace SonicScrewdriver.Tests.Audio;

/// <summary>Second review, finding 5: a microphone muted by the app must not stay muted in competitive mode.</summary>
public class MicMuteGuardTests
{
    private sealed class FakeMic : IMicrophone
    {
        public bool Available { get; set; } = true;
        public string Name => "Mic";
        public float Volume { get; set; } = 0.6f;
        public bool Muted { get; set; }
    }

    [Fact]
    public void A_mic_muted_through_the_app_is_unmuted_when_competitive_mode_starts()
    {
        var mic = new FakeMic();
        var guard = new MicMuteGuard();
        MicrophoneToggle.Toggle(guard.Wrap(mic));          // the shortcut mutes it
        Assert.True(mic.Muted);
        Assert.True(guard.ReleaseIfMutedByApp(mic));
        Assert.False(mic.Muted);
        Assert.False(guard.MutedByApp);
    }

    [Fact]
    public void A_mic_muted_elsewhere_is_left_alone()
    {
        var mic = new FakeMic { Muted = true };            // muted in the Windows settings
        var guard = new MicMuteGuard();
        Assert.False(guard.ReleaseIfMutedByApp(mic));
        Assert.True(mic.Muted);
    }

    [Fact]
    public void Unmuting_through_the_app_clears_the_record()
    {
        var mic = new FakeMic();
        var guard = new MicMuteGuard();
        var wrapped = guard.Wrap(mic);
        MicrophoneToggle.Toggle(wrapped);
        MicrophoneToggle.Toggle(wrapped);
        mic.Muted = true;                                  // muted again from Windows
        Assert.False(guard.ReleaseIfMutedByApp(mic));
        Assert.True(mic.Muted);
    }

    [Fact]
    public void Nothing_is_written_when_the_mic_is_gone_and_the_record_is_dropped()
    {
        var mic = new FakeMic();
        var guard = new MicMuteGuard();
        MicrophoneToggle.Toggle(guard.Wrap(mic));
        mic.Available = false;
        Assert.False(guard.ReleaseIfMutedByApp(mic));
        Assert.False(guard.MutedByApp);
    }
}
