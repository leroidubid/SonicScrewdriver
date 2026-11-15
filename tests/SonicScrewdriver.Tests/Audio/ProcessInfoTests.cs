using System.Diagnostics;
using SonicScrewdriver.Audio;

namespace SonicScrewdriver.Tests.Audio;

public class ProcessInfoTests : IDisposable
{
    public void Dispose() => ProcessInfo.Minimal = false;

    private static string OwnPath => Process.GetCurrentProcess().MainModule!.FileName;

    [Fact]
    public void ImagePathOf_returns_the_executable_path_with_the_minimal_right()
        => Assert.Equal(OwnPath, ProcessInfo.ImagePathOf(Environment.ProcessId), ignoreCase: true);

    [Fact]
    public void ImagePathOf_unknown_process_returns_null()
        => Assert.Null(ProcessInfo.ImagePathOf(int.MaxValue));

    [Fact]
    public void Remember_reads_the_path_and_the_name_from_the_file()
    {
        ProcessInfo.Remember(Environment.ProcessId, "ss-test-normal.exe");
        Assert.Equal(OwnPath, ProcessInfo.ImagePath("ss-test-normal.exe"), ignoreCase: true);
    }

    [Fact]
    public void Minimal_mode_never_looks_up_a_path()
    {
        ProcessInfo.Minimal = true;
        ProcessInfo.Remember(Environment.ProcessId, "ss-test-minimal.exe");
        Assert.Null(ProcessInfo.ImagePath("ss-test-minimal.exe"));
        Assert.Equal("ss-test-minimal", ProcessInfo.DisplayName("ss-test-minimal.exe"));
    }

    [Fact]
    public void Minimal_mode_does_not_block_a_later_lookup()
    {
        ProcessInfo.Minimal = true;
        ProcessInfo.Remember(Environment.ProcessId, "ss-test-later.exe"); // skipped, and nothing cached
        ProcessInfo.Minimal = false;
        ProcessInfo.Remember(Environment.ProcessId, "ss-test-later.exe");
        Assert.NotNull(ProcessInfo.ImagePath("ss-test-later.exe"));
    }

    [Fact]
    public void IsRunning_asks_the_process_list()
    {
        Assert.True(ProcessInfo.IsRunning(Environment.ProcessId));
        Assert.False(ProcessInfo.IsRunning(int.MaxValue));
    }
}
