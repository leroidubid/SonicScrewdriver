using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.Settings;

/// <summary>
/// A shortcut on any chord (modifiers plus any named key, e.g. "Ctrl+Alt+F9") that performs one action on a target.
/// Unlike <see cref="KeyBinding"/> it is not tied to the media keys; it is registered with RegisterHotKey.
/// </summary>
public sealed record FreeHotkey(string Chord, MediaKey Action, Target Target, int StepPercent = 5)
{
    /// <summary>The chord as a <see cref="Hotkey"/>; false when the text is not a valid chord.</summary>
    public bool TryGetHotkey(out Hotkey hotkey) => Hotkey.TryParse(Chord, out hotkey);

    /// <summary>The binding the dispatcher runs: the chord's modifiers are only informational there.</summary>
    public KeyBinding ToBinding(Modifiers modifiers) => new(modifiers, Target, StepPercent);

    /// <summary>Short action label for summaries.</summary>
    public static string ActionLabel(MediaKey action) => action switch
    {
        MediaKey.VolumeUp => "Vol+",
        MediaKey.VolumeDown => "Vol−",
        MediaKey.Mute => "Mute",
        MediaKey.PlayPause => "Play/Pause",
        MediaKey.Next => "Next",
        _ => "Previous",
    };
}

/// <summary>What RegisterHotKey is given: one entry per valid, distinct chord, in settings order.</summary>
public sealed class FreeHotkeyMap
{
    public static FreeHotkeyMap Empty { get; } = new(Array.Empty<Entry>());

    public sealed record Entry(Hotkey Hotkey, MediaKey Action, KeyBinding Binding);

    private readonly IReadOnlyList<Entry> _entries;

    private FreeHotkeyMap(IReadOnlyList<Entry> entries) => _entries = entries;

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Invalid chords and later duplicates of a chord are skipped (the first one wins).</summary>
    public static FreeHotkeyMap From(IEnumerable<FreeHotkey>? hotkeys)
    {
        var seen = new HashSet<Hotkey>();
        var entries = new List<Entry>();
        foreach (var f in hotkeys ?? Array.Empty<FreeHotkey>())
        {
            if (f is null || f.Target is null || !f.TryGetHotkey(out var h) || !seen.Add(h)) continue;
            entries.Add(new Entry(h, f.Action, f.ToBinding(h.Modifiers)));
        }
        return entries.Count == 0 ? Empty : new FreeHotkeyMap(entries);
    }

    public Entry? Find(Hotkey hotkey) => _entries.FirstOrDefault(e => e.Hotkey == hotkey);
}
