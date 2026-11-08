namespace SonicScrewdriver.UI;

public interface IOsd
{
    /// <summary>Shows a transient overlay; <paramref name="exe"/> selects the icon (null = generic speaker).</summary>
    void Show(string? exe, string title, string detail);
}
