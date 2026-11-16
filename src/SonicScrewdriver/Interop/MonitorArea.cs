using System.Runtime.InteropServices;
using System.Windows;

namespace SonicScrewdriver.Interop;

/// <summary>The cursor and the work area (pixels) of the monitor it is on, for placing popups next to the tray icon.</summary>
public static class MonitorArea
{
    /// <summary>Falls back to a tiny rectangle around the cursor when Windows gives no monitor.</summary>
    public static (Point Cursor, Rect WorkArea) AtCursor()
    {
        GetCursorPos(out var pt);
        var work = new Rect(pt.X - 1, pt.Y - 1, 2, 2);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST), ref info))
            work = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top);
        return (new Point(pt.X, pt.Y), work);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
