using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SonicScrewdriver.Cli;

/// <summary>
/// Hands a command from a second launch to the running instance with WM_COPYDATA: the running instance owns a hidden
/// message-only window, the second launch finds it by name and its SendMessage returns the exit code. No pipe, no
/// service, no thread: the message is handled on the UI thread.
/// </summary>
public static class CommandChannel
{
    private const string WindowName = "SonicScrewdriverCommand";
    private const int WM_COPYDATA = 0x004A;
    private const uint SMTO_ABORTIFHUNG = 2;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly IntPtr Magic = new(0x53534344); // "SSCD"

    /// <summary>The window of the running instance; dispose it on exit. <paramref name="handler"/> runs on the UI thread.</summary>
    public static HwndSource CreateServer(Func<string[], ExitCode> handler)
    {
        var window = new HwndSource(new HwndSourceParameters(WindowName) { ParentWindow = HwndMessage, Width = 0, Height = 0 });
        window.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != WM_COPYDATA) return IntPtr.Zero;
            handled = true;
            var data = Marshal.PtrToStructure<CopyData>(lParam);
            if (data.dwData != Magic || data.cbData <= 0 || data.cbData > 4096 || data.lpData == IntPtr.Zero) return new IntPtr((int)ExitCode.Usage);
            var text = Marshal.PtrToStringUni(data.lpData, data.cbData / 2);
            try { return new IntPtr((int)handler(text.Split('\0'))); }
            catch (Exception) { return new IntPtr((int)ExitCode.NoAnswer); }
        });
        return window;
    }

    /// <summary>Sends the arguments to the running instance and returns its answer; NotRunning when there is none.</summary>
    public static ExitCode Send(string[] args)
    {
        var target = FindWindowEx(HwndMessage, IntPtr.Zero, null, WindowName);
        if (target == IntPtr.Zero) return ExitCode.NotRunning;
        var text = string.Join("\0", args);
        var buffer = Marshal.StringToHGlobalUni(text);
        try
        {
            var data = new CopyData { dwData = Magic, cbData = text.Length * 2, lpData = buffer };
            var ok = SendMessageTimeout(target, WM_COPYDATA, IntPtr.Zero, ref data, SMTO_ABORTIFHUNG, 5000, out var result);
            return ok == IntPtr.Zero ? ExitCode.NoAnswer : (ExitCode)(int)result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Prints on the console that started this process (a WinExe has none of its own); silent when launched from Explorer.</summary>
    public static void Print(string message)
    {
        if (!AttachConsole(-1)) return;
        try { using var err = Console.OpenStandardError(); var bytes = System.Text.Encoding.UTF8.GetBytes(message + Environment.NewLine); err.Write(bytes, 0, bytes.Length); err.Flush(); }
        catch (Exception) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, ref CopyData lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
