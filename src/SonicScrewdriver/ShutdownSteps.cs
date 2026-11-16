namespace SonicScrewdriver;

/// <summary>Runs the clean-up steps of the exit one after the other: a step that throws never skips the next ones.</summary>
public static class ShutdownSteps
{
    public static void RunAll(params Action?[] steps)
    {
        foreach (var step in steps)
        {
            try { step?.Invoke(); }
            catch (Exception) { /* the process is ending: keep going with the other restores */ }
        }
    }
}
