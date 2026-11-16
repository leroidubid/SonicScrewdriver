using SonicScrewdriver.Cli;

namespace SonicScrewdriver.Tests.Cli;

/// <summary>Second review, finding 6: a command either runs and reports, or times out and never runs later.</summary>
public class CommandChannelTests
{
    [Fact]
    public void A_message_handled_after_its_deadline_is_not_run()
    {
        var ran = 0;
        var code = CommandChannel.Dispatch("deadline=1000\0--toggle-competitive", 1001, _ => { ran++; return ExitCode.Ok; });
        Assert.Equal(ExitCode.NoAnswer, code);
        Assert.Equal(0, ran);                       // the client was told "no answer": nothing happened
    }

    [Fact]
    public void A_message_within_its_deadline_runs_with_its_own_arguments()
    {
        string[]? got = null;
        var code = CommandChannel.Dispatch("deadline=1000\0--mute\0spotify.exe", 900, a => { got = a; return ExitCode.Ok; });
        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal(new[] { "--mute", "spotify.exe" }, got);
    }

    [Fact]
    public void A_message_without_a_deadline_still_runs()
    {
        string[]? got = null;
        CommandChannel.Dispatch("--mute\0spotify.exe", long.MaxValue, a => { got = a; return ExitCode.Ok; });
        Assert.Equal(new[] { "--mute", "spotify.exe" }, got);
    }

    [Fact]
    public void A_broken_deadline_is_a_usage_error()
    {
        Assert.Equal(ExitCode.Usage, CommandChannel.Dispatch("deadline=abc\0--mute", 0, _ => ExitCode.Ok));
    }
}
