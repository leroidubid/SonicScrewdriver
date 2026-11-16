using SonicScrewdriver.Cli;
using SonicScrewdriver.Tests.Actions;

namespace SonicScrewdriver.Tests.Cli;

public class CommandLineTests
{
    private static Command? Parse(params string[] args) => CommandLine.Parse(args, out _);

    [Fact]
    public void Set_normalizes_the_exe_and_reads_the_percentage()
    {
        Assert.Equal(new Command(CommandKind.Set, "spotify.exe", 30), Parse("--set", "Spotify", "30"));
        Assert.Equal(new Command(CommandKind.Set, "spotify.exe", 30), Parse("--SET", "spotify.exe", "30%"));
        Assert.Equal(100, Parse("--set", "a", "100")!.Percent);
        Assert.Equal(0, Parse("--set", "a", "0")!.Percent);
    }

    [Theory]
    [InlineData("--set")]
    [InlineData("--set", "a")]
    [InlineData("--set", "a", "101")]
    [InlineData("--set", "a", "-5")]
    [InlineData("--set", "a", "x")]
    [InlineData("--set", "a", "5", "6")]
    [InlineData("--mute")]
    [InlineData("--mute", "a", "b")]
    [InlineData("--profile")]
    [InlineData("--toggle-competitive", "x")]
    [InlineData("--nope")]
    public void Invalid_calls_return_a_message(params string[] args)
    {
        Assert.Null(CommandLine.Parse(args, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Other_commands_parse()
    {
        Assert.Equal(CommandKind.Mute, Parse("--mute", "spotify.exe")!.Kind);
        Assert.Equal(CommandKind.Unmute, Parse("--unmute", "spotify.exe")!.Kind);
        Assert.Equal(CommandKind.ToggleCompetitive, Parse("--toggle-competitive")!.Kind);
        Assert.Equal(CommandKind.MuteAll, Parse("--mute-all")!.Kind);
        Assert.Equal(new Command(CommandKind.Profile, "Game night"), Parse("--profile", "Game", "night"));
        Assert.Equal(new Command(CommandKind.Profile, "Game night"), Parse("--profile", "Game night"));
    }

    [Fact]
    public void Only_a_first_argument_starting_with_dashes_is_a_command()
    {
        Assert.False(CommandLine.IsCommand(Array.Empty<string>()));
        Assert.False(CommandLine.IsCommand(new[] { "file.txt" }));
        Assert.True(CommandLine.IsCommand(new[] { "--set" }));
    }

    [Fact]
    public void ToArgs_round_trips()
    {
        foreach (var c in new[]
        {
            new Command(CommandKind.Set, "a.exe", 42), new Command(CommandKind.Mute, "a.exe"), new Command(CommandKind.Profile, "Game night"),
            new Command(CommandKind.ToggleCompetitive), new Command(CommandKind.MuteAll), new Command(CommandKind.MicMute),
        })
            Assert.Equal(c, Parse(c.ToArgs()));
    }

    [Fact]
    public void Messages_exist_for_every_failure()
    {
        Assert.Null(CommandLine.Describe(ExitCode.Ok, null));
        Assert.Contains("not running", CommandLine.Describe(ExitCode.NotRunning, null));
        Assert.Contains("spotify.exe", CommandLine.Describe(ExitCode.NotFound, new Command(CommandKind.Mute, "spotify.exe")));
        Assert.Contains("Game night", CommandLine.Describe(ExitCode.NotFound, new Command(CommandKind.Profile, "Game night")));
    }
}

public class CommandRunnerTests
{
    private static (CommandRunner Runner, FakeAudio Audio, List<bool> Competitive) Make(bool competitive = false)
    {
        var audio = new FakeAudio();
        audio.Apps["spotify.exe"] = (0.8f, false);
        var calls = new List<bool>();
        var state = competitive;
        var runner = new CommandRunner(audio, () => state, on => { state = on; calls.Add(on); });
        return (runner, audio, calls);
    }

    [Fact]
    public void Set_changes_the_volume_and_unmutes()
    {
        var (runner, audio, _) = Make();
        audio.Apps["spotify.exe"] = (0.8f, true);
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.Set, "spotify.exe", 30)));
        Assert.Equal((0.3f, false), audio.Apps["spotify.exe"]);
    }

    [Fact]
    public void Set_to_zero_stays_muted_state_untouched()
    {
        var (runner, audio, _) = Make();
        audio.Apps["spotify.exe"] = (0.8f, true);
        runner.Run(new Command(CommandKind.Set, "spotify.exe", 0));
        Assert.Equal((0f, true), audio.Apps["spotify.exe"]);
    }

    [Fact]
    public void Set_goes_through_the_limit()
    {
        var (runner, audio, _) = Make();
        audio.Limits.Set(new[] { new SonicScrewdriver.Settings.AppLimit("spotify.exe", 40) });
        runner.Run(new Command(CommandKind.Set, "spotify.exe", 90));
        Assert.Equal(0.4f, audio.Apps["spotify.exe"].Vol);
    }

    [Fact]
    public void Mute_unmute_and_toggle()
    {
        var (runner, audio, _) = Make();
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.Mute, "spotify.exe")));
        Assert.True(audio.Apps["spotify.exe"].Muted);
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.ToggleMute, "spotify.exe")));
        Assert.False(audio.Apps["spotify.exe"].Muted);
        runner.Run(new Command(CommandKind.Mute, "spotify.exe"));
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.Unmute, "spotify.exe")));
        Assert.False(audio.Apps["spotify.exe"].Muted);
    }

    [Fact]
    public void Unknown_app_is_not_found()
    {
        var (runner, _, _) = Make();
        Assert.Equal(ExitCode.NotFound, runner.Run(new Command(CommandKind.Set, "nothing.exe", 10)));
        Assert.Equal(ExitCode.NotFound, runner.Run(new Command(CommandKind.Mute, "nothing.exe")));
        Assert.Equal(ExitCode.NotFound, runner.Run(new Command(CommandKind.Profile, "nope")));
    }

    [Fact]
    public void Competitive_mode_refuses_everything_but_its_own_toggle()
    {
        var (runner, audio, calls) = Make(competitive: true);
        Assert.Equal(ExitCode.Refused, runner.Run(new Command(CommandKind.Set, "spotify.exe", 10)));
        Assert.Equal(0.8f, audio.Apps["spotify.exe"].Vol);
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.ToggleCompetitive)));
        Assert.Equal(new[] { false }, calls);
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.ToggleCompetitive)));
        Assert.Equal(new[] { false, true }, calls);
    }

    [Fact]
    public void Profile_and_mute_all_use_the_host_callbacks()
    {
        var (runner, _, _) = Make();
        runner.ApplyProfile = name => name == "Game night";
        runner.ToggleMuteAll = () => true;
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.Profile, "Game night")));
        Assert.Equal(ExitCode.Ok, runner.Run(new Command(CommandKind.MuteAll)));
    }
}
