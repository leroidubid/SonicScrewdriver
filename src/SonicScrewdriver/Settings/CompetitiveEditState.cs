namespace SonicScrewdriver.Settings;

/// <summary>
/// The competitive-mode checkbox of the settings window against the live mode (a shortcut, the tray or a program can
/// toggle it while the window is open). The box counts as "edited" only when the user changed it since it was last in
/// step with the live mode: otherwise a Save keeps the live mode, and the box follows it.
/// </summary>
public sealed class CompetitiveEditState
{
    private bool _baseline;

    public CompetitiveEditState(bool live) => Box = _baseline = live;

    /// <summary>What the checkbox shows.</summary>
    public bool Box { get; set; }

    /// <summary>True when the user changed the box since the baseline (opening, last save, or the last live change).</summary>
    public bool Edited => Box != _baseline;

    /// <summary>The mode a Save must apply: the box when it was edited, otherwise the live mode.</summary>
    public bool ValueToSave(bool live) => Edited ? Box : live;

    /// <summary>The live mode changed from outside: an untouched box follows it, an edited one keeps the user's choice.</summary>
    public void LiveChanged(bool live)
    {
        if (!Edited) Box = _baseline = live;
    }

    /// <summary>After a save: box and baseline are the mode that was applied.</summary>
    public void Saved(bool applied) => Box = _baseline = applied;
}
