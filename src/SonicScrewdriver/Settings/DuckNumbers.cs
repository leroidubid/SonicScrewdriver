namespace SonicScrewdriver.Settings;

/// <summary>The four numbers that ducking and streamer mode share: bounds, clamping and the messages for out-of-range values.</summary>
internal static class DuckNumbers
{
    public const int MinThreshold = 1, MaxThreshold = 50, MaxAttackMs = 2000, MinReleaseMs = 200, MaxReleaseMs = 10000;

    public static (int To, int Threshold, int Attack, int Release) Clamp(int to, int threshold, int attack, int release) =>
        (Math.Clamp(to, 0, 100), Math.Clamp(threshold, MinThreshold, MaxThreshold), Math.Clamp(attack, 0, MaxAttackMs), Math.Clamp(release, MinReleaseMs, MaxReleaseMs));

    /// <summary>User-facing errors for the numbers, each starting with <paramref name="label"/>.</summary>
    public static IEnumerable<string> Check(string label, int to, int threshold, int attack, int release)
    {
        if (to is < 0 or > 100) yield return $"{label}: the level to lower to must be between 0 and 100 %.";
        if (threshold is < MinThreshold or > MaxThreshold) yield return $"{label}: the speaking threshold must be between {MinThreshold} and {MaxThreshold} %.";
        if (attack is < 0 or > MaxAttackMs) yield return $"{label}: the reaction delay must be between 0 and {MaxAttackMs} ms.";
        if (release is < MinReleaseMs or > MaxReleaseMs) yield return $"{label}: the release delay must be between {MinReleaseMs} and {MaxReleaseMs} ms.";
    }
}
