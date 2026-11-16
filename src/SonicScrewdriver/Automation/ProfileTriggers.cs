using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Automation;

/// <summary>
/// Spots the profiles whose program has just started, from the process names of successive snapshots (the same
/// detection as the automatic competitive mode). A program already running when watching begins is not a start.
/// </summary>
public sealed class ProfileTriggers
{
    private HashSet<string>? _previous;

    public static bool Any(IEnumerable<VolumeProfile> profiles) => profiles.Any(p => p.OnProgramStart is not null);

    /// <summary>Forget what was running (watching stopped): the next snapshot is only a baseline.</summary>
    public void Reset() => _previous = null;

    /// <summary>The profiles to apply for this snapshot.</summary>
    public IReadOnlyList<VolumeProfile> Started(IEnumerable<VolumeProfile> profiles, ISet<string> running)
    {
        var before = _previous;
        _previous = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        if (before is null) return Array.Empty<VolumeProfile>();
        return profiles.Where(p => p.OnProgramStart is { } exe && running.Contains(exe) && !before.Contains(exe)).ToList();
    }
}
