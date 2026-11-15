namespace SonicScrewdriver.Input;

/// <summary>A key combination such as "Ctrl+Alt+F9": at least one modifier plus one named key.</summary>
public readonly record struct Hotkey(Modifiers Modifiers, int VirtualKey)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !KeyNames.TryGetVirtualKey(parts[^1], out var vk)) return false;
        var mods = Modifiers.None;
        foreach (var part in parts[..^1])
        {
            var m = part.ToLowerInvariant() switch
            {
                "ctrl" => Modifiers.Ctrl,
                "alt" => Modifiers.Alt,
                "shift" => Modifiers.Shift,
                "win" => Modifiers.Win,
                _ => Modifiers.None,
            };
            if (m == Modifiers.None || (mods & m) != 0) return false;
            mods |= m;
        }
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    /// <summary>"Ctrl+Alt+Shift+Win+Key", in that fixed order.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(Modifiers.Win)) parts.Add("Win");
        parts.Add(KeyNames.NameOf(VirtualKey) ?? "?");
        return string.Join("+", parts);
    }
}
