using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SonicScrewdriver.Input;

/// <summary>
/// Global shortcuts through RegisterHotKey: Windows itself matches the chord and posts a message, so no keyboard hook is
/// needed and no other key is ever seen (this is what lets the competitive-mode toggle work while the hook is
/// uninstalled). Each chord carries a caller's id, raised by <see cref="Pressed"/>. The hidden window exists only while
/// at least one chord is registered.
/// </summary>
public sealed class HotkeyRegistry : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int ProbeId = 0x7FFF;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    private HwndSource? _window;
    private readonly HotkeyTable _table = new();

    /// <summary>A registered chord was pressed; the argument is the id given to <see cref="Apply"/>.</summary>
    public event Action<string>? Pressed;

    /// <summary>Replaces every registered chord. Returns the chords Windows refused. An empty list unregisters all.</summary>
    public IReadOnlyList<Hotkey> Apply(IReadOnlyList<(Hotkey Chord, string Id)> items)
    {
        Clear();
        if (items.Count == 0) return Array.Empty<Hotkey>();
        _window = NewWindow("SonicScrewdriverHotkeys");
        _window.AddHook(WndProc);
        var failed = _table.Fill(items, (id, h) => RegisterHotKey(_window.Handle, id, ToNative(h.Modifiers), (uint)h.VirtualKey));
        if (_table.Count == 0) DisposeWindow();
        return failed;
    }

    /// <summary>True when this registry registered the chord (it is ours, not another program's).</summary>
    public bool Owns(Hotkey hotkey) => _table.Owns(hotkey);

    /// <summary>True when the chord is one of ours or Windows would accept it (tried and released at once).</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        if (Owns(hotkey)) return true;
        var probe = NewWindow("SonicScrewdriverHotkeyProbe");
        try
        {
            var ok = RegisterHotKey(probe.Handle, ProbeId, ToNative(hotkey.Modifiers), (uint)hotkey.VirtualKey);
            if (ok) UnregisterHotKey(probe.Handle, ProbeId);
            return ok;
        }
        finally { probe.Dispose(); }
    }

    private static HwndSource NewWindow(string name) =>
        new(new HwndSourceParameters(name) { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });

    /// <summary>RegisterHotKey modifier flags (with MOD_NOREPEAT: holding the chord fires once).</summary>
    internal static uint ToNative(Modifiers modifiers)
    {
        var mods = MOD_NOREPEAT;
        if (modifiers.HasFlag(Modifiers.Alt)) mods |= MOD_ALT;
        if (modifiers.HasFlag(Modifiers.Ctrl)) mods |= MOD_CONTROL;
        if (modifiers.HasFlag(Modifiers.Shift)) mods |= MOD_SHIFT;
        if (modifiers.HasFlag(Modifiers.Win)) mods |= MOD_WIN;
        return mods;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _table.TryGetId((int)wParam, out var id))
        {
            handled = true;
            Pressed?.Invoke(id);
        }
        return IntPtr.Zero;
    }

    private void Clear()
    {
        if (_window is not null)
            foreach (var id in _table.NativeIds) UnregisterHotKey(_window.Handle, id);
        _table.Clear();
        DisposeWindow();
    }

    private void DisposeWindow()
    {
        _window?.Dispose();
        _window = null;
    }

    public void Dispose() => Clear();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

/// <summary>The id-to-chord bookkeeping of a registry, without a window: native ids are 1, 2, 3... in list order.</summary>
internal sealed class HotkeyTable
{
    private readonly Dictionary<int, string> _byNative = new();
    private readonly HashSet<Hotkey> _owned = new();

    public int Count => _byNative.Count;
    public IReadOnlyCollection<int> NativeIds => _byNative.Keys;
    public bool Owns(Hotkey hotkey) => _owned.Contains(hotkey);
    public bool TryGetId(int nativeId, out string id) => _byNative.TryGetValue(nativeId, out id!);

    /// <summary>Registers each chord through <paramref name="register"/>; returns the ones it refused.</summary>
    public List<Hotkey> Fill(IReadOnlyList<(Hotkey Chord, string Id)> items, Func<int, Hotkey, bool> register)
    {
        Clear();
        var failed = new List<Hotkey>();
        for (var i = 0; i < items.Count; i++)
        {
            if (register(i + 1, items[i].Chord))
            {
                _byNative[i + 1] = items[i].Id;
                _owned.Add(items[i].Chord);
            }
            else failed.Add(items[i].Chord);
        }
        return failed;
    }

    public void Clear()
    {
        _byNative.Clear();
        _owned.Clear();
    }
}

/// <summary>Ids of the named shortcuts (profile shortcuts use "profile:" plus the profile name, free ones "free:" plus the index).</summary>
public static class ActionIds
{
    public const string MuteAll = "muteall";
    public const string Mic = "mic";
    public const string ProfilePrefix = "profile:";
    public const string FreePrefix = "free:";
}
