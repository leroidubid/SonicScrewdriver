using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;

namespace SonicScrewdriver.Update;

public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
}

/// <summary>
/// Looks for a newer release on GitHub and installs it. Runs only when the user asks (or ticked "check at startup"), never in
/// competitive mode. The one request sends no identifier: GitHub sees an IP address and the fixed name "SonicScrewdriver".
/// The downloaded installer is checked against the SHA256SUMS.txt of the same release before it is started.
/// </summary>
public sealed class UpdateService
{
    private const long MaxInstallerBytes = 100L * 1024 * 1024;
    private const long MaxChecksumBytes = 64L * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SonicScrewdriver");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The version of the running exe (x.y.z).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>The newest stable release when it is newer than the running one; null when the app is up to date.</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        string json;
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{ReleaseParser.Repo}/releases/latest", timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new UpdateException($"GitHub answered {(int)response.StatusCode}. Try again later.");
            json = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (HttpRequestException) { throw new UpdateException("Could not reach GitHub. Check the connection."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new UpdateException("GitHub did not answer in time."); }
        var release = ReleaseParser.ParseLatest(json);
        return release is not null && ReleaseParser.IsNewer(release.Version, CurrentVersion) ? release : null;
    }

    /// <summary>Downloads the installer into a temporary folder and checks its SHA256; the file is deleted when the check fails.</summary>
    public Task<string> DownloadAsync(ReleaseInfo release, IProgress<int>? progress = null, CancellationToken ct = default) =>
        DownloadVerifiedAsync(release, release.InstallerUrl, release.InstallerName, progress, ct);

    /// <summary>Same for the portable exe of the release (<see cref="ReleaseParser.ExeName"/>).</summary>
    public Task<string> DownloadExeAsync(ReleaseInfo release, IProgress<int>? progress = null, CancellationToken ct = default) =>
        DownloadVerifiedAsync(release, release.ExeUrl ?? throw new UpdateException("This release has no portable exe."), ReleaseParser.ExeName, progress, ct);

    private async Task<string> DownloadVerifiedAsync(ReleaseInfo release, Uri fileUrl, string fileName, IProgress<int>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SonicScrewdriver-update");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        try
        {
            var sums = await ReadTextAsync(release.ChecksumsUrl, MaxChecksumBytes, ct);
            var expected = ReleaseParser.HashFor(sums, fileName)
                ?? throw new UpdateException($"{ReleaseParser.ChecksumsName} has no entry for {fileName}.");

            using var response = await Http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new UpdateException($"The download failed ({(int)response.StatusCode}).");
            var total = response.Content.Headers.ContentLength;
            if (total > MaxInstallerBytes) throw new UpdateException("The installer is larger than expected. Nothing was installed.");
            using var sha = SHA256.Create();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    done += read;
                    if (done > MaxInstallerBytes) throw new UpdateException("The installer is larger than expected. Nothing was installed.");
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (total > 0) progress?.Report((int)(done * 100 / total.Value));
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (actual != expected) throw new UpdateException("The download does not match its SHA256 checksum. Nothing was installed.");
            return path;
        }
        catch (HttpRequestException) { Discard(path); throw new UpdateException("The download failed. Check the connection."); }
        catch (IOException ex) { Discard(path); throw new UpdateException("Could not write the installer: " + ex.Message); }
        catch (Exception) { Discard(path); throw; }
    }

    private static async Task<string> ReadTextAsync(Uri url, long maxBytes, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new UpdateException($"{ReleaseParser.ChecksumsName} could not be read ({(int)response.StatusCode}).");
        if (response.Content.Headers.ContentLength > maxBytes) throw new UpdateException($"{ReleaseParser.ChecksumsName} is larger than expected.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static void Discard(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Portable copy: puts the checked exe where the running one is. A running exe can be renamed but not overwritten, so it
    /// becomes <c>.old</c>, the new file takes its name, and the new exe is started with <c>--updated-from &lt;pid&gt;</c>:
    /// it waits for this process to end (the single-instance mutex) and deletes the <c>.old</c> file.
    /// </summary>
    public static void ReplaceRunningExe(string newExePath)
    {
        var target = Environment.ProcessPath ?? throw new UpdateException("The path of the running exe is unknown.");
        if (!string.Equals(Path.GetFileName(target), ReleaseParser.ExeName, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("This is not the release exe (it was started by another program), so it cannot be replaced.");
        var old = target + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(target, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException($"Cannot replace {target}: {ex.Message} Move the exe to a folder you can write to, or download the new version by hand.");
        }
        try { File.Copy(newExePath, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Move(old, target); } catch (Exception) { /* the .old file stays next to the exe */ }
            throw new UpdateException($"Could not write {target}: {ex.Message} The previous version was put back.");
        }
        Process.Start(new ProcessStartInfo(target, UpdateRelaunch.ArgumentFor(Environment.ProcessId)) { UseShellExecute = false })?.Dispose();
    }

    /// <summary>Starts the checked installer silently; it closes this app, replaces the exe and starts the app again.</summary>
    public static void Launch(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /RELAUNCH=1") { UseShellExecute = false })?.Dispose();
    }
}
