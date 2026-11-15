using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SonicScrewdriver.Input;

/// <summary>
/// One global hotkey through RegisterHotKey: Windows itself matches the combination and posts a message, so no
/// keyboard hook is needed and no other key is ever seen. This is what lets the competitive-mode toggle keep working
/// while the keyboard hook is uninstalled.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int Id = 1;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _window =
        new(new HwndSourceParameters("SonicScrewdriverHotkey") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey() => _window.AddHook(WndProc);

    /// <summary>Replaces the registered combination (null clears it). False when another program already owns it.</summary>
    public bool Register(Hotkey? hotkey)
    {
        if (_registered) UnregisterHotKey(_window.Handle, Id);
        _registered = false;
        if (hotkey is not { } h) return true;
        var mods = MOD_NOREPEAT;
        if (h.Modifiers.HasFlag(Modifiers.Alt)) mods |= MOD_ALT;
        if (h.Modifiers.HasFlag(Modifiers.Ctrl)) mods |= MOD_CONTROL;
        if (h.Modifiers.HasFlag(Modifiers.Shift)) mods |= MOD_SHIFT;
        if (h.Modifiers.HasFlag(Modifiers.Win)) mods |= MOD_WIN;
        _registered = RegisterHotKey(_window.Handle, Id, mods, (uint)h.VirtualKey);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_window.Handle, Id);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
