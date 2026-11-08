using System.ComponentModel;
using System.Runtime.InteropServices;
using SonicScrewdriver.Bindings;
using static SonicScrewdriver.Interop.NativeMethods;

namespace SonicScrewdriver.Input;

/// <summary>
/// Low-level keyboard hook for media keys, running on its own thread with its own message loop,
/// so a busy UI thread can never make Windows silently drop the hook (LowLevelHooksTimeout).
/// Callbacks run on the hook thread and must be fast: <c>resolve</c> only looks up a binding and
/// <c>onAction</c> must post the real work elsewhere.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    /// <summary>Unassigned virtual key injected to "mask" a lone Alt/Win press (as AutoHotkey does).</summary>
    private const byte VK_MASK = 0xE8;

    private readonly Func<Modifiers, KeyBinding?> _resolve;
    private readonly Action<KeyBinding, MediaKey> _onAction;
    private readonly LowLevelKeyboardProc _proc; // kept in a field so the GC never collects it
    private readonly KeyLatch<KeyBinding> _latch = new(); // touched only on the hook thread
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    public KeyboardHook(Func<Modifiers, KeyBinding?> resolve, Action<KeyBinding, MediaKey> onAction)
    {
        _resolve = resolve;
        _onAction = onAction;
        _proc = HookProc;
    }

    /// <summary>Starts the hook thread; throws if the hook cannot be installed.</summary>
    public void Install()
    {
        if (_thread is not null) return;
        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) error = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "SonicScrewdriver keyboard hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait();
        if (error is not null)
        {
            _thread = null;
            throw error;
        }
    }

    public static Modifiers CurrentModifiers()
    {
        var m = Modifiers.None;
        if (IsKeyDown(VK_SHIFT)) m |= Modifiers.Shift;
        if (IsKeyDown(VK_CONTROL)) m |= Modifiers.Ctrl;
        if (IsKeyDown(VK_MENU)) m |= Modifiers.Alt;
        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN)) m |= Modifiers.Win;
        return m;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var vk = (int)data.vkCode;

            // Injected events are processed too: vendor keyboard utilities and AutoHotkey
            // commonly inject media keys. SonicScrewdriver only injects VK_MASK, which is not a media key.
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (MediaKeyMap.FromVirtualKey(vk) is { } key)
                {
                    try
                    {
                        Modifiers mods = Modifiers.None;
                        var binding = _latch.OnDown(vk, () => _resolve(mods = CurrentModifiers()), out var isRepeat);
                        if (binding is not null)
                        {
                            if (!isRepeat && (mods & (Modifiers.Alt | Modifiers.Win)) != 0) InjectMask();
                            // Volume keys repeat while held; toggles and track skips act once per press.
                            if (!isRepeat || key is MediaKey.VolumeUp or MediaKey.VolumeDown) _onAction(binding, key);
                            return (IntPtr)1;
                        }
                    }
                    catch (Exception) { /* never let the hook throw: fall through to Windows */ }
                }
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                if (_latch.OnUp(vk)) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Without this, a swallowed key leaves Windows seeing a lone Alt press (menu bar gets focus)
    /// or a lone Win press (Start menu opens on release).
    /// </summary>
    private static void InjectMask()
    {
        keybd_event(VK_MASK, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MASK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }
}
