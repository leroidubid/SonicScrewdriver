namespace SonicScrewdriver.Settings;

/// <summary>
/// Small size-capped error log next to settings.json: one entry per error (time, where, message, stack). It never
/// throws, and an identical error repeated within a minute is written once (a timer that fails every 100 ms).
/// Stays on the machine: nothing is sent anywhere.
/// </summary>
public sealed class ErrorLog
{
    public const int MaxBytes = 100_000;
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);

    public static ErrorLog Default { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SonicScrewdriver", "log.txt"));

    private readonly string _path;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, DateTime> _last = new();
    private readonly object _lock = new();

    public ErrorLog(string path, Func<DateTime>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTime.Now);
    }

    /// <summary>Runs a callback of a non-UI thread: an exception is logged instead of ending the process.</summary>
    public void Guard(string source, Action work)
    {
        try { work(); }
        catch (Exception ex) { Write(source, ex); }
    }

    public void Write(string source, Exception ex)
    {
        try
        {
            lock (_lock)
            {
                var now = _now();
                var key = source + "|" + ex.GetType().FullName + "|" + ex.Message;
                if (_last.TryGetValue(key, out var seen) && now - seen < Quiet) return;
                if (_last.Count > 50) _last.Clear();
                _last[key] = now;

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                System.IO.File.AppendAllText(_path, $"[{now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}{Environment.NewLine}{Environment.NewLine}");
                if (new System.IO.FileInfo(_path).Length > MaxBytes) Truncate();
            }
        }
        catch (Exception) { /* a log must never be the reason for a failure */ }
    }

    /// <summary>Keeps the newest half, starting at an entry boundary.</summary>
    private void Truncate()
    {
        var text = System.IO.File.ReadAllText(_path);
        var tail = text[^Math.Min(text.Length, MaxBytes / 2)..];
        var cut = tail.IndexOf("\n[", StringComparison.Ordinal);
        System.IO.File.WriteAllText(_path, cut < 0 ? tail : tail[(cut + 1)..]);
    }
}
