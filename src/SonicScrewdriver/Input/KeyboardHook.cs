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

    /// <summary>Tags the media keys this hook injects for a remapped key, so it never processes its own events.</summary>
    private static readonly UIntPtr InjectedMarker = new(0x5353_4B52);

    private readonly Func<KeyDecider> _decider;
    private readonly Action<KeyBinding, MediaKey> _onAction;
    private readonly LowLevelKeyboardProc _proc; // kept in a field so the GC never collects it
    private readonly KeyLatch<KeyDecision> _latch = new(); // touched only on the hook thread
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    public KeyboardHook(Func<KeyDecider> decider, Action<KeyBinding, MediaKey> onAction)
    {
        _decider = decider;
        _onAction = onAction;
        _proc = HookProc;
    }

    /// <summary>Starts the hook thread; throws if the hook cannot be installed.</summary>
    public void Install()
    {
        if (_thread is not null) return;
        _latch.Clear(); // a press cut short by an earlier Dispose must not linger
        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var hook = _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            // This thread's own hook: if Dispose gave up waiting (1 s) and a new Install already replaced _hook, leave that one alone.
            UnhookWindowsHookEx(hook);
            Interlocked.CompareExchange(ref _hook, IntPtr.Zero, hook);
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

            if ((nuint)(nint)data.dwExtraInfo == (nuint)InjectedMarker) return CallNextHookEx(_hook, nCode, wParam, lParam);

            // Other injected events are processed too: vendor keyboard utilities and AutoHotkey
            // commonly inject media keys. SonicScrewdriver only injects VK_MASK (not a media key) and
            // the tagged media keys above.
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                var decider = _decider();
                if (decider.IsHandled(vk))
                {
                    try
                    {
                        Modifiers mods = Modifiers.None;
                        var decision = _latch.OnDown(vk, () => decider.Decide(vk, mods = CurrentModifiers()), out var isRepeat);
                        if (decision is not null)
                        {
                            var key = decision.Key;
                            // Volume keys repeat while held; toggles and track skips act once per press.
                            var acts = !isRepeat || key is MediaKey.VolumeUp or MediaKey.VolumeDown;
                            if (decision.Binding is { } binding)
                            {
                                if (!isRepeat && (mods & (Modifiers.Alt | Modifiers.Win)) != 0) InjectMask();
                                if (acts) _onAction(binding, key);
                            }
                            else if (acts) SendMediaKey(key, up: false); // remapped key, no shortcut: act as the real key
                            return (IntPtr)1;
                        }
                    }
                    catch (Exception) { /* never let the hook throw: fall through to Windows */ }
                }
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                if (_latch.OnUp(vk, out var decision))
                {
                    if (decision!.Binding is null) SendMediaKey(decision.Key, up: true);
                    return (IntPtr)1;
                }
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

    /// <summary>Sends the real media key for a remapped one, tagged so the hook lets it through.</summary>
    private static void SendMediaKey(MediaKey key, bool up)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki = new KEYBDINPUT
        {
            wVk = (ushort)MediaKeyMap.ToVirtualKey(key),
            dwFlags = KEYEVENTF_EXTENDEDKEY | (up ? KEYEVENTF_KEYUP : 0),
            dwExtraInfo = InjectedMarker,
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }
}
