using SonicScrewdriver.Bindings;

namespace SonicScrewdriver.Settings;

/// <summary>An app of the ducking list: a voice source (its speech triggers ducking) and/or an app that is ducked.</summary>
public sealed record DuckApp(string ExeName, bool Voice, bool Ducked);

/// <summary>
/// Automatic ducking: while a voice app speaks (its session peak level is above <see cref="ThresholdPercent"/>) the
/// apps marked "ducked" are lowered to <see cref="DuckToPercent"/> % of their level, then put back
/// <see cref="ReleaseMs"/> after the voice stops. Off by default.
/// </summary>
public sealed record DuckingSettings(bool Enabled = false, int DuckToPercent = 30, int ThresholdPercent = 4, int AttackMs = 100, int ReleaseMs = 1500, List<DuckApp>? Apps = null)
{
    public const int MinThreshold = DuckNumbers.MinThreshold, MaxThreshold = DuckNumbers.MaxThreshold, MaxAttackMs = DuckNumbers.MaxAttackMs, MinReleaseMs = DuckNumbers.MinReleaseMs, MaxReleaseMs = DuckNumbers.MaxReleaseMs;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<DuckApp> AppList => Apps ?? new List<DuckApp>();

    /// <summary>Clamps the numbers, normalizes exe names, drops rows that are neither voice nor ducked and duplicates (the first wins).</summary>
    public static DuckingSettings Sanitize(DuckingSettings? d)
    {
        d ??= new DuckingSettings();
        var seen = new HashSet<string>();
        var apps = new List<DuckApp>();
        foreach (var a in d.AppList)
        {
            var exe = Target.NormalizeExe(a?.ExeName ?? "");
            if (exe.Length > 0 && (a!.Voice || a.Ducked) && seen.Add(exe)) apps.Add(new DuckApp(exe, a.Voice, a.Ducked));
        }
        var n = DuckNumbers.Clamp(d.DuckToPercent, d.ThresholdPercent, d.AttackMs, d.ReleaseMs);
        return new DuckingSettings(d.Enabled, n.To, n.Threshold, n.Attack, n.Release, apps);
    }

    /// <summary>User-facing errors; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        errors.AddRange(DuckNumbers.Check("Ducking", DuckToPercent, ThresholdPercent, AttackMs, ReleaseMs));
        var seen = new HashSet<string>();
        for (var i = 0; i < AppList.Count; i++)
        {
            var exe = Target.NormalizeExe(AppList[i]?.ExeName ?? "");
            if (exe.Length == 0) errors.Add($"Ducking, row {i + 1}: enter an app name, e.g. discord.exe.");
            else if (!seen.Add(exe)) errors.Add($"Ducking, row {i + 1}: {exe} is already in the list.");
            else if (AppList[i] is { Voice: false, Ducked: false }) errors.Add($"Ducking, row {i + 1}: tick Voice or Ducked for {exe}.");
        }
        if (Enabled && !AppList.Any(a => a is { Voice: true })) errors.Add("Ducking: add at least one voice app (the one whose speech lowers the others).");
        if (Enabled && !AppList.Any(a => a is { Ducked: true })) errors.Add("Ducking: add at least one app to lower.");
        return errors;
    }
}
