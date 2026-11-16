using System.Collections.Concurrent;

namespace SonicScrewdriver.Audio;

/// <summary>
/// What the device notifications (any thread) tell the rebuild of the device trackers (UI thread): "something changed"
/// and which devices changed state (their tracker must not be reused: an unplug and replug of the same device gives the
/// same id but new COM objects).
/// </summary>
internal sealed class DeviceChangeLog
{
    private readonly ConcurrentDictionary<string, byte> _stale = new();
    private volatile bool _dirty = true;

    public bool Dirty => _dirty;

    public void MarkDirty() => _dirty = true;

    /// <summary>Notification thread: the id (when known) first, then the flag.</summary>
    public void Mark(string? deviceId)
    {
        if (deviceId is not null) _stale[deviceId] = 0;
        _dirty = true;
    }

    /// <summary>
    /// Starts a rebuild: the flag is cleared first, then the changed ids are taken. A notification that lands at any point
    /// afterwards sets the flag again (and keeps its id for the next rebuild); one that landed before is in the ids.
    /// <paramref name="afterTake"/> is a test hook that runs right after the ids were taken.
    /// </summary>
    public HashSet<string> Begin(Action? afterTake = null)
    {
        _dirty = false;
        var ids = new HashSet<string>(_stale.Keys);
        foreach (var id in ids) _stale.TryRemove(id, out _);
        afterTake?.Invoke();
        return ids;
    }
}
