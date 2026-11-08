using System.Collections.Concurrent;
using System.Diagnostics;
using SonicScrewdriver.Bindings;

namespace SonicScrewdriver.Audio;

/// <summary>Process lookups. Display names and image paths are cached by exe name.</summary>
internal static class ProcessInfo
{
    private static readonly ConcurrentDictionary<string, (string Display, string? Path)> ByExe = new();

    /// <summary>Normalized exe name for a PID (no cache: PIDs get reused).</summary>
    public static string? ExeName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Target.NormalizeExe(p.ProcessName);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Caches the display name (file description) and image path for an exe, once.</summary>
    public static void Remember(int pid, string exe)
    {
        if (ByExe.ContainsKey(exe)) return;
        string display = System.IO.Path.GetFileNameWithoutExtension(exe);
        string? path = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            path = p.MainModule?.FileName;
            if (path is not null)
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) display = desc.Trim();
            }
        }
        catch (Exception) { /* access denied or exited: keep fallback */ }
        ByExe[exe] = (display, path);
    }

    public static string DisplayName(string exe) =>
        ByExe.TryGetValue(exe, out var v) ? v.Display : System.IO.Path.GetFileNameWithoutExtension(exe);

    public static string? ImagePath(string exe) => ByExe.TryGetValue(exe, out var v) ? v.Path : null;
}
