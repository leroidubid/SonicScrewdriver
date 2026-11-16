namespace SonicScrewdriver.Settings;

/// <summary>
/// Saving the settings: the JSON file first, then the optional "run at logon" registry entry in its own step. A failure of
/// one step never undoes or skips the other; each failure comes back as a message for the user.
/// </summary>
public static class SaveSteps
{
    public static List<string> Run(Action saveFile, Action? changeStartup)
    {
        var problems = new List<string>();
        try { saveFile(); }
        catch (Exception ex) { problems.Add($"Could not save settings: {ex.Message}. Changes apply until Sonic Screwdriver restarts."); }
        if (changeStartup is null) return problems;
        try { changeStartup(); }
        catch (Exception ex) { problems.Add($"Settings saved, but \"Start with Windows\" could not be changed: {ex.Message}"); }
        return problems;
    }
}
