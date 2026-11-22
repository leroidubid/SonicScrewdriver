using System.IO;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Settings;

public class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-log-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_dir, "log.txt");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Writes_one_entry_with_time_source_message_and_stack()
    {
        var now = new DateTime(2026, 11, 22, 10, 0, 0);
        var log = new ErrorLog(LogPath, () => now);
        Exception ex;
        try { throw new InvalidOperationException("boom"); } catch (Exception e) { ex = e; }
        log.Write("Timer", ex);
        var text = File.ReadAllText(LogPath);
        Assert.Contains("[2026-11-22 10:00:00] Timer:", text);
        Assert.Contains("boom", text);
        Assert.Contains("ErrorLogTests", text); // stack
    }

    [Fact]
    public void An_identical_error_within_a_minute_is_written_once_and_again_later()
    {
        var now = DateTime.Now;
        var log = new ErrorLog(LogPath, () => now);
        var ex = new InvalidOperationException("same");
        for (var i = 0; i < 100; i++) { now = now.AddMilliseconds(100); log.Write("Timer", ex); }
        Assert.Equal(1, Count(File.ReadAllText(LogPath), "same"));
        now = now.AddMinutes(2);
        log.Write("Timer", ex);
        Assert.Equal(2, Count(File.ReadAllText(LogPath), "same"));
    }

    [Fact]
    public void Different_messages_are_not_limited()
    {
        var log = new ErrorLog(LogPath);
        log.Write("A", new Exception("one"));
        log.Write("A", new Exception("two"));
        var text = File.ReadAllText(LogPath);
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [Fact]
    public void The_file_stays_near_the_cap_and_keeps_the_newest_entries_whole()
    {
        var log = new ErrorLog(LogPath);
        for (var i = 0; i < 400; i++) log.Write("S", new Exception("entry-" + i + new string('x', 500)));
        var text = File.ReadAllText(LogPath);
        Assert.True(text.Length <= ErrorLog.MaxBytes + 1000);
        Assert.Contains("entry-399", text);
        Assert.DoesNotContain("entry-0x", text);
        Assert.StartsWith("[", text); // starts at an entry boundary
    }

    [Fact]
    public void A_path_that_cannot_be_written_never_throws()
    {
        var blocker = Path.Combine(_dir, "file");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "x");
        var log = new ErrorLog(Path.Combine(blocker, "log.txt")); // the parent is a file
        log.Write("S", new Exception("x"));
    }

    [Fact]
    public void Guard_logs_what_a_callback_throws_and_lets_the_caller_continue()
    {
        var log = new ErrorLog(LogPath);
        log.Guard("Media sessions", () => throw new InvalidOperationException("gone"));
        Assert.Contains("Media sessions", File.ReadAllText(LogPath));
        var ran = false;
        log.Guard("x", () => ran = true);
        Assert.True(ran);
    }

    private static int Count(string text, string what) => text.Split(what).Length - 1;
}
