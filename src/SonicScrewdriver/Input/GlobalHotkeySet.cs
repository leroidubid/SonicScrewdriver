using System.Windows.Interop;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Input;

/// <summary>
/// The free hotkeys, each registered with RegisterHotKey (Windows matches the chord: no keyboard hook, and the chord's
/// keys are consumed only while it is registered). The hidden window exists only while at least one chord is registered.
/// </summary>
public sealed class GlobalHotkeySet : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int ProbeId = 0x7FFF;

    private HwndSource? _window;
    private FreeHotkeyMap _map = FreeHotkeyMap.Empty;
    private readonly List<int> _ids = new();

    /// <summary>A registered chord was pressed.</summary>
    public event Action<FreeHotkeyMap.Entry>? Pressed;

    /// <summary>
    /// Replaces every registered chord with those of <paramref name="map"/>; an empty map unregisters all.
    /// Returns the chords that Windows refused (already owned by Windows or another program).
    /// </summary>
    public IReadOnlyList<Hotkey> Apply(FreeHotkeyMap map)
    {
        Clear();
        var failed = new List<Hotkey>();
        if (map.Entries.Count == 0) return failed;
        EnsureWindow();
        for (var i = 0; i < map.Entries.Count; i++)
        {
            var h = map.Entries[i].Hotkey;
            if (GlobalHotkey.RegisterHotKey(_window!.Handle, i + 1, GlobalHotkey.ToNative(h.Modifiers), (uint)h.VirtualKey))
            {
                _ids.Add(i + 1);
            }
            else failed.Add(h);
        }
        _map = map;
        if (_ids.Count == 0) DisposeWindow();
        return failed;
    }

    /// <summary>True when the chord is one of ours or Windows would accept it (tried and released at once).</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        for (var i = 0; i < _map.Entries.Count; i++)
            if (_map.Entries[i].Hotkey == hotkey && _ids.Contains(i + 1)) return true;
        var probe = new HwndSource(new HwndSourceParameters("SonicScrewdriverHotkeyProbe") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
        try
        {
            var ok = GlobalHotkey.RegisterHotKey(probe.Handle, ProbeId, GlobalHotkey.ToNative(hotkey.Modifiers), (uint)hotkey.VirtualKey);
            if (ok) GlobalHotkey.UnregisterHotKey(probe.Handle, ProbeId);
            return ok;
        }
        finally { probe.Dispose(); }
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new HwndSource(new HwndSourceParameters("SonicScrewdriverFreeHotkeys") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var index = (int)wParam - 1;
            if (index >= 0 && index < _map.Entries.Count && _ids.Contains(index + 1))
            {
                handled = true;
                Pressed?.Invoke(_map.Entries[index]);
            }
        }
        return IntPtr.Zero;
    }

    private void Clear()
    {
        if (_window is not null)
            foreach (var id in _ids) GlobalHotkey.UnregisterHotKey(_window.Handle, id);
        _ids.Clear();
        _map = FreeHotkeyMap.Empty;
        DisposeWindow();
    }

    private void DisposeWindow()
    {
        _window?.Dispose();
        _window = null;
    }

    public void Dispose() => Clear();
}
