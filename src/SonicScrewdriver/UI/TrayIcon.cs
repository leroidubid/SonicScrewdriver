using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SonicScrewdriver.UI;

/// <summary>
/// Native notification-area icon (Shell_NotifyIcon, with a WPF menu window), without WinForms.
/// The mouse wheel over the icon is read through Raw Input, registered only while the cursor
/// hovers the icon, so it costs nothing the rest of the time.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 1;
    private const int WM_INPUT = 0x00FF;
    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    private const uint IconId = 1;

    private readonly HwndSource _window;
    private readonly IntPtr _hIcon;
    private IntPtr _hRedIcon; // competitive mode, built on first use
    private IntPtr _hGreyIcon; // microphone muted, built on first use
    private bool _micMuted;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _wheelArmed;
    private bool _wheelEnabled = true;
    private bool _competitive;
    private IReadOnlyList<string> _shortcuts = Array.Empty<string>();
    private string _tip = "Sonic Screwdriver";
    private int _wheelRemainder;
    private uint _lastClickTick;
    private TrayMenuWindow? _menu;

    /// <summary>Name of the current default output device, shown at the top of the menu.</summary>
    public Func<string?>? DefaultDeviceName { get; set; }

    /// <summary>The competitive-mode shortcut, shown next to its menu entry (null when none).</summary>
    public string? ModeHotkeyText { get; set; }

    /// <summary>Names of the volume profiles listed in the menu (read each time it opens).</summary>
    public Func<IReadOnlyList<string>>? ProfileNames { get; set; }

    /// <summary>A profile was chosen in the menu.</summary>
    public event EventHandler<string>? ProfileRequested;

    public event EventHandler? OpenFlyoutRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ExitRequested;
    /// <summary>The user toggled competitive mode from the menu; the argument is the new state.</summary>
    public event EventHandler<bool>? CompetitiveModeToggled;
    /// <summary>Wheel over the icon: +1 per notch up, -1 per notch down.</summary>
    public event EventHandler<int>? WheelScrolled;

    public TrayIcon()
    {
        // Hidden top-level popup (not message-only) so it also receives the TaskbarCreated broadcast.
        _window = new HwndSource(new HwndSourceParameters("SonicScrewdriverTray") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
        _hIcon = LoadAppIcon();
        if (_hIcon == IntPtr.Zero) _hIcon = CreateIcon();
        _hoverTimer.Tick += (_, _) => { if (!CursorOverIcon()) DisarmWheel(); };
        AddIcon();
    }

    /// <summary>False in competitive mode: the wheel over the icon is never read (no Raw Input registration).</summary>
    public bool WheelEnabled
    {
        get => _wheelEnabled;
        set
        {
            _wheelEnabled = value;
            if (!value) DisarmWheel();
        }
    }

    /// <summary>Competitive mode: the tooltip and the menu say the shortcuts are off, and the menu item is checked.</summary>
    public void SetCompetitive(bool on)
    {
        _competitive = on;
        UpdateTip();
        UpdateIcon();
    }

    /// <summary>The microphone is muted (a grey icon) or not; competitive mode's red icon wins while it is on.</summary>
    public void SetMicMuted(bool muted)
    {
        if (muted == _micMuted) return;
        _micMuted = muted;
        UpdateIcon();
    }

    private IntPtr CurrentIcon()
    {
        if (_competitive && _hRedIcon == IntPtr.Zero) _hRedIcon = Tint(_hIcon);
        if (_micMuted && _hGreyIcon == IntPtr.Zero) _hGreyIcon = Tint(_hIcon, grey: true);
        if (_competitive && _hRedIcon != IntPtr.Zero) return _hRedIcon;
        return _micMuted && _hGreyIcon != IntPtr.Zero ? _hGreyIcon : _hIcon;
    }

    private void UpdateIcon()
    {
        var data = NewData(NIF_ICON);
        data.hIcon = CurrentIcon();
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private const string CompetitiveLine = "Competitive mode: shortcuts off";

    private IReadOnlyList<string> ActiveLines => _competitive ? new[] { CompetitiveLine } : _shortcuts;

    /// <summary>Shows the configured shortcuts in the tooltip and at the top of the menu.</summary>
    public void SetShortcuts(IReadOnlyList<string> lines)
    {
        _shortcuts = lines;
        UpdateTip();
    }

    /// <summary>Refreshes the tooltip (the default device can change at any time); no-op when unchanged.</summary>
    private void UpdateTip()
    {
        // Competitive mode: no audio enumeration on every hover, the tip is just the mode line.
        var tip = _competitive ? CompetitiveLine : Bindings.ShortcutSummary.Tooltip(DefaultDeviceName?.Invoke(), ActiveLines);
        if (tip == _tip) return;
        _tip = tip;
        var data = NewData(NIF_TIP);
        data.szTip = _tip;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Notify(string title, string text)
    {
        var data = NewData(NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void AddIcon()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = WM_APP_TRAY;
        data.hIcon = CurrentIcon();
        data.szTip = _tip;
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        szTip = "", szInfo = "", szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            switch ((int)lParam)
            {
                case WM_LBUTTONUP:
                    // A double-click sends two button-ups: treat it as one click.
                    var now = (uint)Environment.TickCount;
                    if (now - _lastClickTick > GetDoubleClickTime()) OpenFlyoutRequested?.Invoke(this, EventArgs.Empty);
                    _lastClickTick = now;
                    break;
                case WM_RBUTTONUP: ShowMenu(); break;
                case WM_MOUSEMOVE:
                    if (!_wheelArmed) UpdateTip(); // once per hover, before the tooltip appears
                    ArmWheel();
                    break;
            }
            handled = true;
        }
        else if (msg == WM_INPUT && _wheelArmed)
        {
            var delta = ReadWheel(lParam);
            if (delta != 0 && CursorOverIcon())
            {
                // High-resolution wheels send small deltas: one step per full 120-unit notch.
                _wheelRemainder += delta;
                var notches = _wheelRemainder / 120;
                _wheelRemainder %= 120;
                if (notches != 0) WheelScrolled?.Invoke(this, notches);
            }
        }
        else if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            AddIcon(); // Explorer restarted: put the icon back
        }
        return IntPtr.Zero;
    }

    /// <summary>Right-click: the menu is a WPF window in the style of the mixer flyout, created on first use.</summary>
    private void ShowMenu()
    {
        if (_menu is null)
        {
            _menu = new TrayMenuWindow();
            _menu.SettingsRequested += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            _menu.CompetitiveToggleRequested += (_, _) => CompetitiveModeToggled?.Invoke(this, !_competitive);
            _menu.ProfileRequested += (_, name) => ProfileRequested?.Invoke(this, name);
            _menu.QuitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        GetCursorPos(out var pt);
        // The cursor sits on the taskbar: the menu is placed inside the work area of that monitor.
        var work = new Rect(pt.X - 1, pt.Y - 1, 2, 2);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST), ref monitorInfo))
            work = new Rect(monitorInfo.rcWork.Left, monitorInfo.rcWork.Top,
                monitorInfo.rcWork.Right - monitorInfo.rcWork.Left, monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top);
        _menu.ShowAt(new Point(pt.X, pt.Y), work, DefaultDeviceName?.Invoke() ?? "none", ActiveLines, _competitive, ModeHotkeyText, ProfileNames?.Invoke() ?? Array.Empty<string>());
    }

    // --- Mouse wheel over the icon -------------------------------------------------------------

    private void ArmWheel()
    {
        if (_wheelArmed || !_wheelEnabled) return;
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = _window.Handle };
        _wheelArmed = RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        if (_wheelArmed) _hoverTimer.Start();
    }

    private void DisarmWheel()
    {
        _hoverTimer.Stop();
        if (!_wheelArmed) return;
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _wheelArmed = false;
    }

    private static int ReadWheel(IntPtr hRawInput)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 1024) return 0;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size) return 0;
            if (Marshal.ReadInt32(buffer) != RIM_TYPEMOUSE) return 0;
            // RAWMOUSE: usFlags (2) + padding (2), then usButtonFlags (2), usButtonData (2).
            var flags = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + 4);
            if ((flags & RI_MOUSE_WHEEL) == 0) return 0;
            var delta = Marshal.ReadInt16(buffer, (int)headerSize + 6);
            return delta;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private bool CursorOverIcon()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _window.Handle, uID = IconId };
        if (Shell_NotifyIconGetRect(ref id, out var r) < 0) return false;
        GetCursorPos(out var pt);
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    // --- Icon -----------------------------------------------------------------------------------

    /// <summary>The exe's embedded application icon at the small-icon size for the current DPI.</summary>
    private static IntPtr LoadAppIcon()
    {
        const int IDI_APPLICATION_RESOURCE = 32512; // ID the .NET SDK gives the ApplicationIcon resource
        var cx = GetSystemMetrics(SM_CXSMICON);
        var cy = GetSystemMetrics(SM_CYSMICON);
        // LoadImage (user32) picks the best frame; LoadIconWithScaleDown would need comctl32 v6.
        return LoadImage(GetModuleHandle(null), (IntPtr)IDI_APPLICATION_RESOURCE, IMAGE_ICON, cx, cy, 0);
    }

    /// <summary>Fallback: speaker glyph (Segoe MDL2 Assets) with an accent dot, rendered by WPF into an HICON.</summary>
    private static IntPtr CreateIcon()
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var text = new FormattedText("", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe MDL2 Assets"), 22, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(10, 132, 255)), null, new Point(26, 26), 5, 5);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);

        var color = CreateBitmap(size, size, 1, 32, pixels);
        var mask = CreateBitmap(size, size, 1, 1, new byte[size * size / 8]);
        try
        {
            var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(color);
            DeleteObject(mask);
        }
    }

    /// <summary>
    /// A red copy of an icon, used while competitive mode is on. Only the colored parts (the blue emitter and the sound
    /// arcs) turn red; the steel and grip stay neutral, so the pixel-art shapes stay as readable as in the normal icon
    /// and the white hot core of the emitter stays white.
    /// </summary>
    private static IntPtr Tint(IntPtr icon, bool grey = false)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var bmp = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                int a = px[i + 3];
                if (a == 0) continue;
                int b = px[i] * 255 / a, g = px[i + 1] * 255 / a, r = px[i + 2] * 255 / a; // back to straight alpha
                int sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                if (grey)
                {
                    var l = (r * 30 + g * 59 + b * 11) / 100 * 3 / 4; // a darker grey
                    px[i + 2] = px[i + 1] = px[i] = (byte)(l * a / 255);
                    continue;
                }
                double k = Math.Clamp((sat - 30) / 60.0, 0, 1); // 0 for grays, 1 for the saturated blue
                int nr = (int)(r + (b - r) * k), ng = (int)(g + (g / 4 - g) * k), nb = (int)(b + (r * 3 / 5 - b) * k);
                px[i + 2] = (byte)(nr * a / 255);
                px[i + 1] = (byte)(ng * a / 255);
                px[i] = (byte)(nb * a / 255);
            }
            var color = CreateBitmap(w, h, 1, 32, px);
            var mask = CreateBitmap(w, h, 1, 1, new byte[(w + 15) / 16 * 2 * h]);
            try
            {
                var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
                return CreateIconIndirect(ref info);
            }
            finally
            {
                DeleteObject(color);
                DeleteObject(mask);
            }
        }
        catch { return IntPtr.Zero; }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        DisarmWheel();
        var data = NewData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        DestroyIcon(_hIcon);
        if (_hRedIcon != IntPtr.Zero) DestroyIcon(_hRedIcon);
        if (_hGreyIcon != IntPtr.Zero) DestroyIcon(_hGreyIcon);
        _window.Dispose();
    }

    // --- Interop --------------------------------------------------------------------------------

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    private const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100;
    private const uint RID_INPUT = 0x10000003;
    private const int RIM_TYPEMOUSE = 0;
    private const ushort RI_MOUSE_WHEEL = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const uint IMAGE_ICON = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int cx, int cy, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
