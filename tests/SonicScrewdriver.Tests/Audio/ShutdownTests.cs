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
