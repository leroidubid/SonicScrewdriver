using SonicScrewdriver.Automation;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Automation;

/// <summary>Second review, finding 2: after leaving competitive mode the program-start profiles must be watched again.</summary>
public class AutoPollPolicyTests
{
    private static AppSettings WithTriggeredProfile() => new()
    {
        Profiles = new() { new VolumeProfile("Game", new(), OnProgramStart: "game.exe") },
    };

    [Fact]
    public void Enter_then_leave_competitive_mode_wants_the_poll_again()
    {
        var s = WithTriggeredProfile();
        Assert.True(AutoPollPolicy.Wanted(s));
        s.CompetitiveMode = true;
        Assert.False(AutoPollPolicy.Wanted(s));   // no lookups in competitive mode
        s.CompetitiveMode = false;
        Assert.True(AutoPollPolicy.Wanted(s));    // the app must re-evaluate this after a manual switch
    }

    [Fact]
    public void An_automatic_list_entry_keeps_the_poll_in_competitive_mode()
    {
        var s = new AppSettings { CompetitiveMode = true, AutoPrograms = new() { new AutoProgram("game.exe", true) } };
        Assert.True(AutoPollPolicy.Wanted(s));
    }

    [Fact]
    public void Nothing_configured_means_no_poll()
    {
        Assert.False(AutoPollPolicy.Wanted(new AppSettings()));
    }
}
