using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SonicScrewdriver.Interop;

/// <summary>Returns unused memory to Windows while the app sits idle in the tray.</summary>
internal static class MemoryTrim
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    public static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var p = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
    }
}
