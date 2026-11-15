using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SonicScrewdriver.Bindings;
using SonicScrewdriver.Interop;

namespace SonicScrewdriver.Audio;

/// <summary>Process lookups. Display names and image paths are cached by exe name.</summary>
internal static class ProcessInfo
{
    private static readonly ConcurrentDictionary<string, (string Display, string? Path)> ByExe = new();

    /// <summary>
    /// Competitive mode: never open another process. Exe names still come from the process list (no handle),
    /// but no image path, friendly name or icon is looked up.
    /// </summary>
    public static bool Minimal { get; set; }

    /// <summary>Normalized exe name for a PID, from the process list (no handle). Not cached: PIDs get reused.</summary>
    public static string? ExeName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Target.NormalizeExe(p.ProcessName);
        }
        catch (Exception) { return null; }
    }

    /// <summary>True while the process exists. Asks the process list, so the process itself is not opened.</summary>
    public static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Full path of a process image, using only PROCESS_QUERY_LIMITED_INFORMATION (what Task Manager uses).
    /// It never grants access to the process memory, which anti-cheat software strips and may log.
    /// </summary>
    public static string? ImagePathOf(int pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally { NativeMethods.CloseHandle(handle); }
    }

    /// <summary>Caches the friendly name and image path of an exe, once. The name is read from the file on disk.</summary>
    public static void Remember(int pid, string exe)
    {
        if (Minimal || ByExe.ContainsKey(exe)) return;
        var display = System.IO.Path.GetFileNameWithoutExtension(exe);
        var path = ImagePathOf(pid);
        if (path is not null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) display = description.Trim();
            }
            catch (Exception) { /* unreadable file: keep the exe name */ }
        }
        ByExe[exe] = (display, path);
    }

    public static string DisplayName(string exe) =>
        ByExe.TryGetValue(exe, out var v) ? v.Display : System.IO.Path.GetFileNameWithoutExtension(exe);

    public static string? ImagePath(string exe) => ByExe.TryGetValue(exe, out var v) ? v.Path : null;
}
