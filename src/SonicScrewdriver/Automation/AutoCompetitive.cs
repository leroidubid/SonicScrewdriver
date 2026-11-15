using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Automation;

/// <summary>
/// Decides when competitive mode follows the programs of the automatic list. It turns the mode on when an enabled
/// program starts and off when the last one exits, but only if it was the one that turned it on: a manual switch
/// (tray menu, shortcut, settings) always wins, and a manual "off" holds until the program has exited.
/// </summary>
public sealed class AutoCompetitive
{
    private bool _auto;       // the mode is on because of this class
    private bool _suppressed; // the user switched it off while a listed program runs
    private bool _anyRunning;

    /// <summary>True while the current "on" state comes from a running program (so it must not be saved as a preference).</summary>
    public bool IsAutomatic => _auto;

    /// <summary>The first enabled program that is running, or null.</summary>
    public static string? FirstRunning(IEnumerable<AutoProgram> programs, ISet<string> runningExeNames) =>
        programs.FirstOrDefault(p => p.Enabled && runningExeNames.Contains(p.ExeName))?.ExeName;

    /// <summary>Returns the mode to switch to, or null to leave it as it is.</summary>
    public bool? Evaluate(bool anyRunning, bool modeOn)
    {
        _anyRunning = anyRunning;
        if (!anyRunning)
        {
            _suppressed = false;
            if (!_auto) return null;
            _auto = false;
            return false;
        }
        if (modeOn || _suppressed || _auto) return null;
        _auto = true;
        return true;
    }

    /// <summary>The user switched the mode by hand: the automation lets go of it.</summary>
    public void UserChanged(bool modeOn)
    {
        _auto = false;
        _suppressed = !modeOn && _anyRunning;
    }
}
