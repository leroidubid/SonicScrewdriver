using SonicScrewdriver.Audio;

namespace SonicScrewdriver.Cli;

/// <summary>Executes a parsed command against the running app. UI thread only.</summary>
public sealed class CommandRunner
{
    private readonly IAudioSessionService _audio;
    private readonly Func<bool> _competitive;
    private readonly Action<bool> _setCompetitive;

    public CommandRunner(IAudioSessionService audio, Func<bool> competitive, Action<bool> setCompetitive)
    {
        _audio = audio;
        _competitive = competitive;
        _setCompetitive = setCompetitive;
    }

    /// <summary>Applies the named profile; false when there is none.</summary>
    public Func<string, bool> ApplyProfile { get; set; } = _ => false;
    /// <summary>Mutes everything or restores it; false when there was nothing to mute.</summary>
    public Func<bool> ToggleMuteAll { get; set; } = () => false;
    /// <summary>Toggles the default microphone; false when there is none.</summary>
    public Func<bool> ToggleMic { get; set; } = () => false;

    public ExitCode Run(Command command)
    {
        if (command.Kind == CommandKind.ToggleCompetitive)
        {
            _setCompetitive(!_competitive());
            return ExitCode.Ok;
        }
        // Like the shortcuts, commands are off in competitive mode.
        if (_competitive()) return ExitCode.Refused;

        var exe = command.Target ?? "";
        switch (command.Kind)
        {
            case CommandKind.Set:
                if (!_audio.SetVolume(exe, _audio.Limits.Clamp(exe, command.Percent / 100f))) return ExitCode.NotFound;
                if (command.Percent > 0 && _audio.GetMute(exe) == true) _audio.SetMute(exe, false);
                return ExitCode.Ok;
            case CommandKind.Mute: return _audio.SetMute(exe, true) ? ExitCode.Ok : ExitCode.NotFound;
            case CommandKind.Unmute: return _audio.SetMute(exe, false) ? ExitCode.Ok : ExitCode.NotFound;
            case CommandKind.ToggleMute: return _audio.ToggleMute(exe) is null ? ExitCode.NotFound : ExitCode.Ok;
            case CommandKind.Profile: return ApplyProfile(exe) ? ExitCode.Ok : ExitCode.NotFound;
            case CommandKind.MuteAll: return ToggleMuteAll() ? ExitCode.Ok : ExitCode.NotFound;
            case CommandKind.MicMute: return ToggleMic() ? ExitCode.Ok : ExitCode.NotFound;
            default: return ExitCode.Usage;
        }
    }
}
