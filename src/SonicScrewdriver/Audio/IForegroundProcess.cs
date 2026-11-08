namespace SonicScrewdriver.Audio;

public interface IForegroundProcess
{
    /// <summary>Normalized exe name ("chrome.exe") of the foreground window's process, or null.</summary>
    string? GetForegroundExe();
}
