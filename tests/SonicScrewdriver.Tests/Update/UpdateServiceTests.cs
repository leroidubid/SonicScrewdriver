using System.IO;
using System.Net;
using System.Security.Cryptography;
using SonicScrewdriver.Update;
using Xunit;

namespace SonicScrewdriver.Tests.Update;

/// <summary>The download and its checksum check, against a local HTTP listener (no internet).</summary>
public class UpdateServiceTests
{
    private static async Task<(string path, Exception? error)> Run(byte[] served, string sumsText)
    {
        var port = new Random().Next(20000, 40000);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var loop = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var ctx = await listener.GetContextAsync();
                var body = ctx.Request.Url!.AbsolutePath.EndsWith("SHA256SUMS.txt") ? System.Text.Encoding.UTF8.GetBytes(sumsText) : served;
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            }
        });
        var release = new ReleaseInfo(new Version(9, 9, 9), "v9.9.9", "SonicScrewdriver-Setup-9.9.9.exe",
            new Uri($"http://localhost:{port}/SonicScrewdriver-Setup-9.9.9.exe"), new Uri($"http://localhost:{port}/SHA256SUMS.txt"));
        try { return (await new UpdateService().DownloadAsync(release), null); }
        catch (Exception ex) { return ("", ex); }
        finally { listener.Stop(); try { await loop; } catch (Exception) { } }
    }

    private static string Sums(byte[] data) => $"{Convert.ToHexString(SHA256.HashData(data)).ToLower()}  SonicScrewdriver-Setup-9.9.9.exe\n";

    [Fact]
    public async Task A_download_with_the_published_hash_is_kept()
    {
        var data = new byte[200_000]; new Random(1).NextBytes(data);
        var (path, error) = await Run(data, Sums(data));
        Assert.Null(error);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task A_download_with_another_hash_is_refused_and_deleted()
    {
        var data = new byte[1000]; var other = new byte[1000]; other[0] = 1;
        var (_, error) = await Run(data, Sums(other));
        Assert.IsType<UpdateException>(error);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "SonicScrewdriver-update", "SonicScrewdriver-Setup-9.9.9.exe")));
    }

    [Fact]
    public async Task A_checksum_file_without_the_installer_is_refused()
    {
        var data = new byte[1000];
        var (_, error) = await Run(data, new string('a', 64) + "  Other.exe\n");
        Assert.IsType<UpdateException>(error);
    }
}
