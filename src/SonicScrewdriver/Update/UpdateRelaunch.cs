using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace SonicScrewdriver.Update;

/// <summary>What a portable update needs around the swap of the exe, and how to tell an installed copy from a portable one.</summary>
public static class UpdateRelaunch
{
    private const string Flag = "--updated-from";

    /// <summary>Uninstall key of the Inno Setup installer (AppId of installer/SonicScrewdriver.iss).</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C1F2E4A-5B3D-4E8F-9A2C-6D0B1E3F5A47}_is1";

    public static string ArgumentFor(int pid) => $"{Flag} {pid.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>True for <c>--updated-from &lt;pid&gt;</c>: the process that started this one is being replaced and must end first.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out int pid)
    {
        pid = 0;
        return args.Count == 2 && args[0] == Flag && int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
    }

    /// <summary>Waits (20 s at most) for the previous process to end, then removes the <c>.old</c> exe it left.</summary>
    public static void WaitAndClean(int pid)
    {
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(20_000);
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { }
        try { if (Environment.ProcessPath is { } path) File.Delete(path + ".old"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>True when <paramref name="exePath"/> sits in the install folder written by the installer. Pure.</summary>
    public static bool IsInstalledCopy(string? exePath, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(installLocation)) return false;
        try
        {
            var dir = Path.GetFullPath(Path.GetDirectoryName(exePath) ?? "").TrimEnd('\\', '/');
            return string.Equals(dir, Path.GetFullPath(installLocation).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>The running exe is the one the installer put in place (per-user or all-users install).</summary>
    public static bool RunningFromInstall()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(UninstallKey);
                if (IsInstalledCopy(Environment.ProcessPath, key?.GetValue("InstallLocation") as string)) return true;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return false;
    }
}
