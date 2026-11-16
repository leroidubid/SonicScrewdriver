using System.Globalization;
using SonicScrewdriver.Bindings;

namespace SonicScrewdriver.Cli;

public enum CommandKind { Set, Mute, Unmute, ToggleMute, ToggleCompetitive, Profile, MuteAll, MicMute }

/// <summary>One parsed command: <see cref="Target"/> is an exe name (or a profile name), <see cref="Percent"/> only for Set.</summary>
public sealed record Command(CommandKind Kind, string? Target = null, int Percent = 0)
{
    /// <summary>The arguments that rebuild this command (what a second launch hands to the running instance).</summary>
    public string[] ToArgs() => Kind switch
    {
        CommandKind.Set => new[] { "--set", Target!, Percent.ToString(CultureInfo.InvariantCulture) },
        CommandKind.Mute => new[] { "--mute", Target! },
        CommandKind.Unmute => new[] { "--unmute", Target! },
        CommandKind.ToggleMute => new[] { "--toggle-mute", Target! },
        CommandKind.Profile => new[] { "--profile", Target! },
        CommandKind.MuteAll => new[] { "--mute-all" },
        CommandKind.MicMute => new[] { "--mic-mute" },
        _ => new[] { "--toggle-competitive" },
    };
}

/// <summary>Process exit codes of a command line call (0 means done).</summary>
public enum ExitCode
{
    Ok = 0,
    Usage = 1,
    NotRunning = 2,
    NotFound = 3,
    Refused = 4,
    NoAnswer = 5,
}

/// <summary>Parses <c>SonicScrewdriver.exe --set spotify.exe 30</c> and the other commands. Pure: no I/O.</summary>
public static class CommandLine
{
    public const string Usage =
        "Usage: SonicScrewdriver.exe --set <app> <0-100> | --mute <app> | --unmute <app> | --toggle-mute <app> | " +
        "--profile <name> | --mute-all | --mic-mute | --toggle-competitive";

    /// <summary>True when the arguments ask for a command (the first one starts with "--"); otherwise it is a plain launch.</summary>
    public static bool IsCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0].StartsWith("--", StringComparison.Ordinal);

    /// <summary>Returns null and an error message when the arguments are not a valid command.</summary>
    public static Command? Parse(IReadOnlyList<string> args, out string error)
    {
        error = "";
        if (args.Count == 0) { error = Usage; return null; }
        var verb = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();
        switch (verb)
        {
            case "--set":
                if (rest.Count != 2) { error = "--set needs an app and a percentage, e.g. --set spotify.exe 30."; return null; }
                var text = rest[1].Trim().TrimEnd('%');
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) || percent > 100)
                { error = $"\"{rest[1]}\" is not a percentage between 0 and 100."; return null; }
                return App(CommandKind.Set, rest[0], percent, out error);
            case "--mute": return AppOnly(CommandKind.Mute, verb, rest, out error);
            case "--unmute": return AppOnly(CommandKind.Unmute, verb, rest, out error);
            case "--toggle-mute": return AppOnly(CommandKind.ToggleMute, verb, rest, out error);
            case "--profile":
                // A profile name may contain spaces that the caller did not quote: join the rest.
                var name = string.Join(" ", rest).Trim();
                if (name.Length == 0) { error = "--profile needs a profile name, e.g. --profile \"Game night\"."; return null; }
                return new Command(CommandKind.Profile, name);
            case "--mute-all": return NoArgs(CommandKind.MuteAll, verb, rest, out error);
            case "--mic-mute": return NoArgs(CommandKind.MicMute, verb, rest, out error);
            case "--toggle-competitive": return NoArgs(CommandKind.ToggleCompetitive, verb, rest, out error);
            default:
                error = $"Unknown command {args[0]}. {Usage}";
                return null;
        }
    }

    private static Command? App(CommandKind kind, string exe, int percent, out string error)
    {
        error = "";
        var normalized = Target.NormalizeExe(exe);
        if (normalized.Length == 0) { error = "The app name is empty."; return null; }
        return new Command(kind, normalized, percent);
    }

    private static Command? AppOnly(CommandKind kind, string verb, List<string> rest, out string error)
    {
        if (rest.Count != 1) { error = $"{verb} needs one app, e.g. {verb} spotify.exe."; return null; }
        return App(kind, rest[0], 0, out error);
    }

    private static Command? NoArgs(CommandKind kind, string verb, List<string> rest, out string error)
    {
        error = rest.Count == 0 ? "" : $"{verb} takes no argument.";
        return rest.Count == 0 ? new Command(kind) : null;
    }

    /// <summary>The message printed for a result (null when everything went well).</summary>
    public static string? Describe(ExitCode code, Command? command) => code switch
    {
        ExitCode.Ok => null,
        ExitCode.Usage => Usage,
        ExitCode.NotRunning => "Sonic Screwdriver is not running. Start it first (then run the command again).",
        ExitCode.NotFound => command?.Kind switch
        {
            CommandKind.Profile => $"No profile named \"{command.Target}\".",
            CommandKind.MicMute => "No microphone found.",
            CommandKind.MuteAll => "Nothing is playing.",
            _ => $"{command?.Target} is not playing sound right now.",
        },
        ExitCode.Refused => "Competitive mode is on: only --toggle-competitive is accepted. Turn it off first.",
        _ => "Sonic Screwdriver did not answer.",
    };
}
