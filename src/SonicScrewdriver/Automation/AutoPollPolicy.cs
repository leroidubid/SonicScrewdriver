using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Automation;

/// <summary>When the 5 s process-name poll has something to do (decided from the settings and the mode, never remembered).</summary>
public static class AutoPollPolicy
{
    /// <summary>An enabled program of the automatic list, or (outside competitive mode) a profile that starts with a program.</summary>
    public static bool Wanted(AppSettings settings) =>
        settings.AutoPrograms.Any(p => p.Enabled) || (!settings.CompetitiveMode && ProfileTriggers.Any(settings.Profiles));
}
