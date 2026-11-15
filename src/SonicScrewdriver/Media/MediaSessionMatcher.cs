namespace SonicScrewdriver.Media;

public static class MediaSessionMatcher
{
    /// <summary>
    /// True when a GSMTC SourceAppUserModelId belongs to <paramref name="exe"/>:
    /// "Spotify.exe", "Chrome", or a packaged AUMID whose app id (after the bang) is the exe name.
    /// </summary>
    public static bool Matches(string sourceAppUserModelId, string exe)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId) || string.IsNullOrWhiteSpace(exe)) return false;
        var name = System.IO.Path.GetFileNameWithoutExtension(exe.Trim());
        var id = sourceAppUserModelId.Trim();
        var bang = id.LastIndexOf('!');
        var candidates = bang >= 0 ? new[] { id, id[(bang + 1)..] } : new[] { id };
        foreach (var c in candidates)
        {
            var cName = c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? c[..^4] : c;
            if (string.Equals(cName, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        // Firefox registers a 16-hex-digit hash of its install path instead of its name.
        return string.Equals(name, "firefox", StringComparison.OrdinalIgnoreCase) && IsHex16(id);
    }

    /// <summary>
    /// Among the sessions of one app (a browser and its installed web apps share an exe), the one that is playing,
    /// else the first.
    /// </summary>
    public static T? PreferPlaying<T>(IEnumerable<T> candidates, Func<T, bool> isPlaying) where T : class
    {
        var list = candidates.ToList();
        return list.FirstOrDefault(isPlaying) ?? list.FirstOrDefault();
    }

    private static bool IsHex16(string s) => s.Length == 16 && s.All(Uri.IsHexDigit);
}
