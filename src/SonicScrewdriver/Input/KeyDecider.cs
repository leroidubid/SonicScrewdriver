using SonicScrewdriver.Bindings;

namespace SonicScrewdriver.Input;

/// <summary>
/// What the hook does with one key press. <see cref="Binding"/> is the shortcut to run; when it is null the key is a
/// remapped one without a shortcut, and must behave like the real media key (the hook sends that key instead).
/// </summary>
public sealed record KeyDecision(MediaKey Key, KeyBinding? Binding);

/// <summary>Physical key (virtual key) to the media key it stands for, built from the settings.</summary>
public sealed class KeyRemapMap
{
    public static KeyRemapMap Empty { get; } = new(new Dictionary<int, MediaKey>());

    private readonly Dictionary<int, MediaKey> _map;

    private KeyRemapMap(Dictionary<int, MediaKey> map) => _map = map;

    /// <summary>Entries with an unknown key name are ignored; the first entry for a key wins.</summary>
    public static KeyRemapMap From(IEnumerable<(MediaKey Action, string Key)> remaps)
    {
        var map = new Dictionary<int, MediaKey>();
        foreach (var (action, key) in remaps)
            if (KeyNames.TryGetVirtualKey(key, out var vk) && KeyNames.IsRemappable(vk)) map.TryAdd(vk, action);
        return map.Count == 0 ? Empty : new KeyRemapMap(map);
    }

    public bool TryGet(int vk, out MediaKey key) => _map.TryGetValue(vk, out key);

    public string? KeyNameOf(MediaKey action)
    {
        foreach (var (vk, key) in _map) if (key == action) return KeyNames.NameOf(vk);
        return null;
    }
}

/// <summary>
/// The hook's decision for a key-down, as a pure function so it can be tested: a real media key resolves through the
/// bindings; a remapped key resolves through the same bindings, and otherwise becomes the real key.
/// </summary>
public sealed class KeyDecider
{
    private readonly KeyRemapMap _remaps;
    private readonly Func<Modifiers, KeyBinding?> _resolve;

    public KeyDecider(KeyRemapMap remaps, Func<Modifiers, KeyBinding?> resolve)
    {
        _remaps = remaps;
        _resolve = resolve;
    }

    /// <summary>Cheap pre-check: only these keys are ever looked at (and latched).</summary>
    public bool IsHandled(int vk) => MediaKeyMap.FromVirtualKey(vk) is not null || _remaps.TryGet(vk, out _);

    /// <summary>Null means pass the key through to Windows untouched.</summary>
    public KeyDecision? Decide(int vk, Modifiers mods)
    {
        var key = MediaKeyMap.FromVirtualKey(vk);
        var remapped = false;
        if (key is null)
        {
            if (!_remaps.TryGet(vk, out var mapped)) return null;
            key = mapped;
            remapped = true;
        }
        var binding = _resolve(mods);
        if (binding is not null) return new KeyDecision(key.Value, binding);
        return remapped ? new KeyDecision(key.Value, null) : null;
    }
}
