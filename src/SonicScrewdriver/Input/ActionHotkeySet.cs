using System.Windows.Interop;

namespace SonicScrewdriver.Input;

/// <summary>
/// Named global shortcuts (mute everything, microphone, profiles), each registered with RegisterHotKey like the free
/// hotkeys: no keyboard hook. The hidden window exists only while at least one chord is registered.
/// </summary>
public sealed class ActionHotkeySet : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private HwndSource? _window;
    private readonly Dictionary<int, string> _byId = new();
    private readonly HashSet<Hotkey> _owned = new();

    /// <summary>A registered chord was pressed; the argument is the id given to <see cref="Apply"/>.</summary>
    public event Action<string>? Pressed;

    /// <summary>Replaces every registered chord. Returns the chords Windows refused. An empty list unregisters all.</summary>
    public IReadOnlyList<Hotkey> Apply(IReadOnlyList<(Hotkey Chord, string Id)> items)
    {
        Clear();
        var failed = new List<Hotkey>();
        if (items.Count == 0) return failed;
        _window = new HwndSource(new HwndSourceParameters("SonicScrewdriverActionHotkeys") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
        for (var i = 0; i < items.Count; i++)
        {
            var h = items[i].Chord;
            if (GlobalHotkey.RegisterHotKey(_window.Handle, i + 1, GlobalHotkey.ToNative(h.Modifiers), (uint)h.VirtualKey))
            {
                _byId[i + 1] = items[i].Id;
                _owned.Add(h);
            }
            else failed.Add(h);
        }
        if (_byId.Count == 0) DisposeWindow();
        return failed;
    }

    /// <summary>True when this set registered the chord (it is ours, not another program's).</summary>
    public bool Owns(Hotkey hotkey) => _owned.Contains(hotkey);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _byId.TryGetValue((int)wParam, out var id))
        {
            handled = true;
            Pressed?.Invoke(id);
        }
        return IntPtr.Zero;
    }

    private void Clear()
    {
        if (_window is not null)
            foreach (var id in _byId.Keys) GlobalHotkey.UnregisterHotKey(_window.Handle, id);
        _byId.Clear();
        _owned.Clear();
        DisposeWindow();
    }

    private void DisposeWindow()
    {
        _window?.Dispose();
        _window = null;
    }

    public void Dispose() => Clear();
}

/// <summary>Ids of the named shortcuts (profile shortcuts use "profile:" plus the profile name).</summary>
public static class ActionIds
{
    public const string MuteAll = "muteall";
    public const string Mic = "mic";
    public const string ProfilePrefix = "profile:";
}
