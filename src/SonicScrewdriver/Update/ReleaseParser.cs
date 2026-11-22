using System.Text.Json;
using System.Text.RegularExpressions;

namespace SonicScrewdriver.Update;

/// <summary>A stable release that has what the updater needs: the installer and the checksum file, both on the project's GitHub release.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string InstallerName, Uri InstallerUrl, Uri ChecksumsUrl, Uri? ExeUrl = null);

/// <summary>Reads the answer of the GitHub "latest release" API and the SHA256SUMS.txt of a release. No network here.</summary>
public static class ReleaseParser
{
    public const string Repo = "leroidubid/SonicScrewdriver";
    public const string ChecksumsName = "SHA256SUMS.txt";
    public const string ExeName = "SonicScrewdriver.exe";

    private static readonly Regex TagPattern = new(@"^v(\d+\.\d+\.\d+)$", RegexOptions.Compiled);

    public static string InstallerNameFor(Version v) => $"SonicScrewdriver-Setup-{v.Major}.{v.Minor}.{v.Build}.exe";

    /// <summary>
    /// The release described by the JSON, or null when it is a draft or a pre-release, has no x.y.z tag, or lacks the installer
    /// or the checksum file. Only download links under github.com/&lt;repo&gt;/releases/download/&lt;tag&gt;/ are accepted.
    /// </summary>
    public static ReleaseInfo? ParseLatest(string json, string repo = Repo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
            if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String) return null;
            var tag = tagElement.GetString() ?? "";
            var match = TagPattern.Match(tag);
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            var installerName = InstallerNameFor(version);
            var prefix = $"https://github.com/{repo}/releases/download/{tag}/";
            Uri? installer = null, sums = null, exe = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (name is null || url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (url != prefix + name) continue;
                if (name == installerName) installer = new Uri(url);
                else if (name == ChecksumsName) sums = new Uri(url);
                else if (name == ExeName) exe = new Uri(url);
            }
            return installer is null || sums is null ? null : new ReleaseInfo(version, tag, installerName, installer, sums, exe);
        }
        catch (JsonException) { return null; }
        catch (UriFormatException) { return null; }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.True;

    /// <summary>True when <paramref name="latest"/> is a higher x.y.z than the running version (the revision number is ignored).</summary>
    public static bool IsNewer(Version latest, Version current) =>
        new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0)).CompareTo(new Version(current.Major, current.Minor, Math.Max(current.Build, 0))) > 0;

    /// <summary>The lowercase SHA256 of <paramref name="fileName"/> in a "hash  name" file (sha256sum format, optional "*" before the name), or null.</summary>
    public static string? HashFor(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64) continue;
            var name = line[(space + 1)..].TrimStart(' ', '*');
            if (name == fileName && Regex.IsMatch(line[..64], "^[0-9a-fA-F]{64}$")) return line[..64].ToLowerInvariant();
        }
        return null;
    }
}
