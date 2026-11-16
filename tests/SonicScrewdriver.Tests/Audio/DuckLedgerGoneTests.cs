using SonicScrewdriver.Audio;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Audio;

/// <summary>Second review, finding 4: an app with no session at the release must not keep the lowered level for good.</summary>
public class DuckLedgerGoneTests
{
    private long _now;

    private (DuckLedger Ledger, FakeAudio Audio, object Owner) Setup()
    {
        var audio = new FakeAudio();
        audio.Apps["spotify.exe"] = (0.8f, false);
        var ledger = new DuckLedger(audio, () => _now);
        var owner = new object();
        Assert.True(ledger.Lower(owner, "spotify.exe", 0.3f));
        return (ledger, audio, owner);
    }

    [Fact]
    public void Release_with_no_session_waits_and_restores_when_the_session_comes_back()
    {
        var (ledger, audio, owner) = Setup();
        var lowered = audio.Apps["spotify.exe"];
        audio.Apps.Remove("spotify.exe");                       // the app has no session when the engine lets go
        ledger.Release(owner, "spotify.exe");
        Assert.True(ledger.HasWaiting);
        audio.Apps["spotify.exe"] = lowered;                    // it comes back; Windows remembered the lowered level
        _now += 60_000;
        ledger.RetryPending();
        Assert.Equal(0.8f, audio.Apps["spotify.exe"].Vol, 3);   // the level the user had
        Assert.False(ledger.HasWaiting);
    }

    [Fact]
    public void The_wait_is_bounded()
    {
        var (ledger, audio, owner) = Setup();
        var lowered = audio.Apps["spotify.exe"];
        audio.Apps.Remove("spotify.exe");
        ledger.Release(owner, "spotify.exe");
        _now += DuckLedger.WaitMs + 1;
        ledger.RetryPending();                                   // expired: forgotten
        Assert.False(ledger.HasWaiting);
        audio.Apps["spotify.exe"] = lowered;
        ledger.RetryPending();
        Assert.Equal(lowered.Vol, audio.Apps["spotify.exe"].Vol); // too late: left alone
    }

    [Fact]
    public void A_level_the_user_changed_meanwhile_is_kept()
    {
        var (ledger, audio, owner) = Setup();
        audio.Apps.Remove("spotify.exe");
        ledger.Release(owner, "spotify.exe");
        audio.Apps["spotify.exe"] = (0.55f, false);
        ledger.RetryPending();
        Assert.Equal(0.55f, audio.Apps["spotify.exe"].Vol);
        Assert.False(ledger.HasWaiting);
    }

    [Fact]
    public void Lowering_again_while_waiting_takes_the_entry_back()
    {
        var (ledger, audio, owner) = Setup();
        var lowered = audio.Apps["spotify.exe"];
        audio.Apps.Remove("spotify.exe");
        ledger.Release(owner, "spotify.exe");
        audio.Apps["spotify.exe"] = lowered;
        Assert.True(ledger.Lower(owner, "spotify.exe", 0.3f));
        Assert.False(ledger.HasWaiting);
        ledger.Release(owner, "spotify.exe");
        Assert.Equal(0.8f, audio.Apps["spotify.exe"].Vol, 3);   // still the original, not the lowered level
    }

    [Fact]
    public void WaitingChanged_is_raised_when_the_state_changes()
    {
        var (ledger, audio, owner) = Setup();
        var raised = 0;
        ledger.WaitingChanged += () => raised++;
        var lowered = audio.Apps["spotify.exe"];
        audio.Apps.Remove("spotify.exe");
        ledger.Release(owner, "spotify.exe");
        Assert.Equal(1, raised);
        audio.Apps["spotify.exe"] = lowered;
        ledger.RetryPending();
        Assert.Equal(2, raised);
    }
}
