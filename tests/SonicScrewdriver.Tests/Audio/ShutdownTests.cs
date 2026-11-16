using SonicScrewdriver.Audio;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

public class ShutdownTests
{
    [Fact]
    public void Quitting_with_mute_everything_active_restores_every_app_even_if_another_step_throws()
    {
        var audio = new FakeAudio();
        audio.Apps["a.exe"] = (0.5f, false);
        audio.Apps["b.exe"] = (0.5f, true);
        var mute = new MuteAll(audio);
        mute.Toggle();
        Assert.True(audio.Apps["a.exe"].Muted);

        ShutdownSteps.RunAll(() => throw new InvalidOperationException("boom"), () => mute.Restore());

        Assert.False(audio.Apps["a.exe"].Muted);
        Assert.True(audio.Apps["b.exe"].Muted); // was muted before: stays muted
    }
}

public class MuteAllMixedStateTests
{
    [Fact]
    public void An_app_muted_on_one_output_only_gets_back_exactly_that_state()
    {
        var audio = new FakeAudio();
        audio.Apps["a.exe"] = (0.5f, false);
        audio.DeviceList = new()
        {
            new("d1", "One", true, 1, false, new[] { new AudioApp("a.exe", "A", 1, 0.5f, true) }),
            new("d2", "Two", false, 1, false, new[] { new AudioApp("a.exe", "A", 2, 0.5f, false) }),
        };
        var mute = new MuteAll(audio);
        mute.Toggle();
        Assert.Equal(new[] { ("a.exe", true, (string?)"d2") }, audio.MuteCalls);   // the already muted session is left alone
        audio.MuteCalls.Clear();
        mute.Toggle();
        Assert.Equal(new[] { ("a.exe", false, (string?)"d2") }, audio.MuteCalls);  // and stays muted afterwards
    }
}
