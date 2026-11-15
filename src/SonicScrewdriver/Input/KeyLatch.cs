namespace SonicScrewdriver.Input;

/// <summary>
/// Latches the decision made on a key's first key-down for the whole press (auto-repeats and key-up),
/// so changing modifiers mid-hold never splits one press between SonicScrewdriver and Windows.
/// </summary>
public sealed class KeyLatch<T> where T : class
{
    private readonly Dictionary<int, T?> _held = new();

    /// <summary>Returns the latched value (null = pass through). <paramref name="decide"/> runs only on the first down.</summary>
    public T? OnDown(int vk, Func<T?> decide, out bool isRepeat)
    {
        isRepeat = _held.TryGetValue(vk, out var latched);
        if (isRepeat) return latched;
        latched = decide();
        _held[vk] = latched;
        return latched;
    }

    /// <summary>Ends the press; true when its key-down was swallowed (so the key-up must be too).</summary>
    public bool OnUp(int vk) => _held.Remove(vk, out var latched) && latched is not null;

    /// <summary>Forgets every press (used when the hook is reinstalled).</summary>
    public void Clear() => _held.Clear();
}
