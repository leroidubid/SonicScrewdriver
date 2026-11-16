namespace SonicScrewdriver.Audio;

/// <summary>
/// Decides when the default output should become the first plugged-in device of the priority list. It acts only when
/// that top choice changes (a device of the list appears or disappears, or the first evaluation): an output the user
/// picks by hand is kept until then, and a change of the default made by someone else is not fought.
/// </summary>
public sealed class DevicePriorityPolicy
{
    private string? _lastTop;

    /// <summary>The first device of <paramref name="order"/> that is plugged in, or null.</summary>
    public static string? Top(IReadOnlyList<string> order, IReadOnlyCollection<string> activeIds) =>
        order.FirstOrDefault(activeIds.Contains);

    /// <summary>The device to make the default now, or null to leave things as they are.</summary>
    public string? Evaluate(IReadOnlyList<string> order, IReadOnlyCollection<string> activeIds, string? defaultId)
    {
        var top = Top(order, activeIds);
        if (top is null) { _lastTop = null; return null; }
        if (top == _lastTop) return null;
        _lastTop = top;
        return top == defaultId ? null : top;
    }

    /// <summary>Forget the last top choice (watching stopped, or switching failed): the next evaluation acts again.</summary>
    public void Reset() => _lastTop = null;
}
