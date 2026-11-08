namespace SonicScrewdriver.Bindings;

public enum TargetKind
{
    Foreground,
    Process,
    /// <summary>Output devices: Vol+/Vol- cycle the default device, Mute mutes it.</summary>
    OutputDevice,
}

/// <summary>What a shortcut acts on: the foreground app, or a fixed executable.</summary>
public sealed record Target(TargetKind Kind, string? ExeName)
{
    public static Target Foreground { get; } = new(TargetKind.Foreground, null);
    public static Target OutputDevice { get; } = new(TargetKind.OutputDevice, null);

    public static Target Process(string exe) => new(TargetKind.Process, NormalizeExe(exe));

    /// <summary>"Spotify", " SPOTIFY.EXE " → "spotify.exe".</summary>
    public static string NormalizeExe(string exe)
    {
        var name = (exe ?? string.Empty).Trim().ToLowerInvariant();
        if (name.Length == 0) return name;
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
    }
}
