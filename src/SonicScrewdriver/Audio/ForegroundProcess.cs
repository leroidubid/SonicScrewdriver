using System.Text;
using SonicScrewdriver.Interop;

namespace SonicScrewdriver.Audio;

public sealed class ForegroundProcess : IForegroundProcess
{
    public string? GetForegroundExe()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var exe = ProcessInfo.ExeName((int)pid);

        // UWP apps are hosted by ApplicationFrameHost; the real process owns the CoreWindow child.
        if (exe == "applicationframehost.exe")
        {
            uint childPid = 0;
            NativeMethods.EnumChildWindows(hwnd, (child, _) =>
            {
                var cls = new StringBuilder(256);
                NativeMethods.GetClassName(child, cls, cls.Capacity);
                if (cls.ToString() == "Windows.UI.Core.CoreWindow")
                {
                    NativeMethods.GetWindowThreadProcessId(child, out childPid);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (childPid != 0 && childPid != pid)
            {
                var childExe = ProcessInfo.ExeName((int)childPid);
                if (childExe is not null) { exe = childExe; pid = childPid; }
            }
        }
        // Cache its real name and icon for the OSD, even when it has no audio session.
        if (exe is not null) ProcessInfo.Remember((int)pid, exe);
        return exe;
    }
}
