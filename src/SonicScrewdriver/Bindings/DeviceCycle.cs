namespace SonicScrewdriver.Bindings;

public static class DeviceCycle
{
    /// <summary>
    /// Next output device in the cycle. <paramref name="selected"/> empty means every active device.
    /// Returns null when there is nothing to cycle through.
    /// </summary>
    public static string? Next(IReadOnlyList<string> active, string? current, IReadOnlyCollection<string> selected, int direction)
    {
        var ring = selected.Count == 0 ? active.ToList() : active.Where(selected.Contains).ToList();
        if (ring.Count == 0) return null;
        var index = current is null ? -1 : ring.IndexOf(current);
        if (index < 0) return ring[0];
        var step = direction >= 0 ? 1 : -1;
        return ring[((index + step) % ring.Count + ring.Count) % ring.Count];
    }
}
