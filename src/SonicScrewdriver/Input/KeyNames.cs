namespace SonicScrewdriver.Input;

/// <summary>Stable names for the keys the settings can refer to (F1, PageUp, Numpad3, ...), and their virtual keys.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> ByName = Build();
    private static readonly Dictionary<int, string> ByVk = ByName.ToDictionary(p => p.Value, p => p.Key);

    private static Dictionary<string, int> Build()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 24; i++) d["F" + i] = 0x6F + i;
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (var i = 0; i <= 9; i++) d["Numpad" + i] = 0x60 + i;
        d["NumpadMultiply"] = 0x6A;
        d["NumpadAdd"] = 0x6B;
        d["NumpadSubtract"] = 0x6D;
        d["NumpadDecimal"] = 0x6E;
        d["NumpadDivide"] = 0x6F;
        d["Pause"] = 0x13;
        d["ScrollLock"] = 0x91;
        d["Insert"] = 0x2D;
        d["Delete"] = 0x2E;
        d["Home"] = 0x24;
        d["End"] = 0x23;
        d["PageUp"] = 0x21;
        d["PageDown"] = 0x22;
        d["Space"] = 0x20;
        d["Up"] = 0x26;
        d["Down"] = 0x28;
        d["Left"] = 0x25;
        d["Right"] = 0x27;
        return d;
    }

    public static bool TryGetVirtualKey(string? name, out int vk)
    {
        vk = 0;
        return name is not null && ByName.TryGetValue(name.Trim(), out vk);
    }

    public static string? NameOf(int vk) => ByVk.GetValueOrDefault(vk);

    /// <summary>
    /// Keys that may replace a media key: never typing keys (letters, digits, Space, arrows, Enter...),
    /// which would break text input. F1-F24, Pause, Scroll Lock, Insert, Home/End, Page Up/Down and the numpad.
    /// </summary>
    public static bool IsRemappable(int vk) =>
        vk is >= 0x70 and <= 0x87           // F1-F24
        or >= 0x60 and <= 0x6F              // numpad
        or 0x13 or 0x91 or 0x2D or 0x24 or 0x23 or 0x21 or 0x22; // Pause, ScrollLock, Insert, Home, End, PageUp, PageDown
}
