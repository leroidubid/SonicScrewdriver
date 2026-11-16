using SonicScrewdriver.Bindings;

namespace SonicScrewdriver.Settings;

/// <summary>An app of the streamer list: lowered while the user speaks when <see cref="Lowered"/> is ticked.</summary>
public sealed record StreamApp(string ExeName, bool Lowered);

/// <summary>
/// Streamer mode, the reverse of ducking: while the user speaks into the default microphone (its peak level is above
/// <see cref="ThresholdPercent"/>) the ticked apps are lowered to <see cref="DuckToPercent"/> % of their level, then put
/// back <see cref="ReleaseMs"/> after the voice stops. Off by default.
/// </summary>
public sealed record StreamerSettings(bool Enabled = false, int DuckToPercent = 30, int ThresholdPercent = 5, int AttackMs = 150, int ReleaseMs = 1200, List<StreamApp>? Apps = null)
{
    public const int MinThreshold = 1, MaxThreshold = 50, MaxAttackMs = 2000, MinReleaseMs = 200, MaxReleaseMs = 10000;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<StreamApp> AppList => Apps ?? new List<StreamApp>();

    /// <summary>True when the mode is on and at least one app is ticked: the only case where the microphone is watched.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Active => Enabled && AppList.Any(a => a is { Lowered: true });

    /// <summary>The same numbers as a ducking configuration (no voice app: the trigger is the microphone) so the ducking engine can run it.</summary>
    public DuckingSettings ToDucking() => new(Enabled, DuckToPercent, ThresholdPercent, AttackMs, ReleaseMs,
        AppList.Where(a => a.Lowered).Select(a => new DuckApp(a.ExeName, false, true)).ToList());

    /// <summary>Clamps the numbers, normalizes exe names, drops empty names and duplicates (the first wins).</summary>
    public static StreamerSettings Sanitize(StreamerSettings? s)
    {
        s ??= new StreamerSettings();
        var seen = new HashSet<string>();
        var apps = new List<StreamApp>();
        foreach (var a in s.AppList)
        {
            var exe = Target.NormalizeExe(a?.ExeName ?? "");
            if (exe.Length > 0 && seen.Add(exe)) apps.Add(new StreamApp(exe, a!.Lowered));
        }
        return new StreamerSettings(s.Enabled, Math.Clamp(s.DuckToPercent, 0, 100), Math.Clamp(s.ThresholdPercent, MinThreshold, MaxThreshold),
            Math.Clamp(s.AttackMs, 0, MaxAttackMs), Math.Clamp(s.ReleaseMs, MinReleaseMs, MaxReleaseMs), apps);
    }

    /// <summary>User-facing errors; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (DuckToPercent is < 0 or > 100) errors.Add("Streamer mode: the level to lower to must be between 0 and 100 %.");
        if (ThresholdPercent is < MinThreshold or > MaxThreshold) errors.Add($"Streamer mode: the speaking threshold must be between {MinThreshold} and {MaxThreshold} %.");
        if (AttackMs is < 0 or > MaxAttackMs) errors.Add($"Streamer mode: the reaction delay must be between 0 and {MaxAttackMs} ms.");
        if (ReleaseMs is < MinReleaseMs or > MaxReleaseMs) errors.Add($"Streamer mode: the release delay must be between {MinReleaseMs} and {MaxReleaseMs} ms.");
        var seen = new HashSet<string>();
        for (var i = 0; i < AppList.Count; i++)
        {
            var exe = Target.NormalizeExe(AppList[i]?.ExeName ?? "");
            if (exe.Length == 0) errors.Add($"Streamer mode, row {i + 1}: enter an app name, e.g. spotify.exe.");
            else if (!seen.Add(exe)) errors.Add($"Streamer mode, row {i + 1}: {exe} is already in the list.");
        }
        if (Enabled && !AppList.Any(a => a is { Lowered: true })) errors.Add("Streamer mode: tick at least one app to lower while you speak.");
        return errors;
    }
}
