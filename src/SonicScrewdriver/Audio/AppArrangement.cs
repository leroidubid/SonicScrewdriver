namespace SonicScrewdriver.Audio;

/// <summary>Order and filter of the apps listed under a device: hidden ones dropped, pinned ones first.</summary>
public static class AppArrangement
{
    /// <summary>
    /// Hidden apps are removed; pinned apps come first in the order of the pinned list (so the order never
    /// jumps around); the others follow in their original order. Exe names compare case-insensitively.
    /// </summary>
    public static List<T> Arrange<T>(IEnumerable<T> apps, Func<T, string> exeOf, IReadOnlyList<string> pinned, IReadOnlyCollection<string> hidden)
    {
        var visible = apps.Where(a => !hidden.Contains(exeOf(a), StringComparer.OrdinalIgnoreCase)).ToList();
        var pins = new List<T>();
        foreach (var exe in pinned)
        {
            var match = visible.FirstOrDefault(a => string.Equals(exeOf(a), exe, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !pins.Contains(match)) pins.Add(match);
        }
        return pins.Concat(visible.Where(a => !pins.Contains(a))).ToList();
    }
}
