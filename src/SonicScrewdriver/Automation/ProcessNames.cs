using System.Runtime.InteropServices;

namespace SonicScrewdriver.Automation;

/// <summary>
/// Names of the running executables, from a toolhelp snapshot of the process list: no process is ever opened, so
/// there is no handle on any game and no access right to be logged by an anti-cheat.
/// </summary>
public static class ProcessNames
{
    private const uint TH32CS_SNAPPROCESS = 0x2;
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>Lower-case exe names ("valorant.exe"); empty if the snapshot cannot be taken.</summary>
    public static HashSet<string> Running()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandle) return names;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            for (var ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                names.Add(entry.szExeFile.ToLowerInvariant());
        }
        finally { CloseHandle(snapshot); }
        return names;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
