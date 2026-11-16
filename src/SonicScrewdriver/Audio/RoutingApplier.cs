using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Audio;

/// <summary>Reads and sets the output device of one process (the per-app routing of Windows).</summary>
public interface IAppRouter
{
    /// <summary>The MMDevice ID the process is routed to; null when it follows the default device.</summary>
    string? GetDevice(int processId);
    bool SetDevice(int processId, string? deviceId);
}

public sealed class AudioPolicyRouter : IAppRouter
{
    public string? GetDevice(int processId) => AudioPolicy.GetAppDevice(processId);
    public bool SetDevice(int processId, string? deviceId) => AudioPolicy.SetAppDevice(processId, deviceId);
}

/// <summary>
/// Applies the routing rules to apps as their session appears. A process is handled once: if the user then routes it
/// elsewhere by hand, that choice stays until the app restarts. A rule whose device is not plugged in is skipped (and
/// tried again when something changes).
/// </summary>
public sealed class RoutingApplier
{
    private readonly IAppRouter _router;
    private readonly HashSet<int> _handled = new();

    public RoutingApplier(IAppRouter router) => _router = router;

    /// <summary>Forget the processes already handled (the observer stopped).</summary>
    public void Reset() => _handled.Clear();

    /// <summary>Returns the number of processes that were routed.</summary>
    public int Apply(IReadOnlyList<RoutingRule> rules, IReadOnlyCollection<string> activeDeviceIds, Func<string, IReadOnlyList<int>> processIds)
    {
        var routed = 0;
        var alive = new HashSet<int>();
        foreach (var rule in rules)
        {
            foreach (var pid in processIds(rule.ExeName))
            {
                alive.Add(pid);
                if (_handled.Contains(pid) || !activeDeviceIds.Contains(rule.DeviceId)) continue;
                if (_router.GetDevice(pid) == rule.DeviceId) { _handled.Add(pid); continue; }
                if (_router.SetDevice(pid, rule.DeviceId)) { _handled.Add(pid); routed++; }
            }
        }
        _handled.IntersectWith(alive); // exited processes are forgotten (their ids may be reused)
        return routed;
    }
}
