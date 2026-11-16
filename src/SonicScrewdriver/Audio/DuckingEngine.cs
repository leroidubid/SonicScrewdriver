using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Audio;

/// <summary>
/// The levels of the apps that are lowered, shared by every engine that lowers apps (automatic ducking and streamer
/// mode). It remembers the level the user had (the original) and what the owners asked for: an app lowered by two
/// owners goes to the original times the lowest factor (never both, so no double ducking), and when the last owner
/// lets go it returns to the original, whatever the order the owners stop in.
/// </summary>
public sealed class DuckLedger
{
    private const float Tolerance = 0.012f;

    /// <summary>How long a restore waits for an app that has no session at the moment of the release (ms).</summary>
    public const long WaitMs = 5 * 60 * 1000;

    private sealed class Entry
    {
        public float Original, Applied;
        public bool Pending; // lowered level still there, the restore failed while the app was running
        public long? WaitUntil; // the app had no session at the release: restored when it comes back, until this time (ms)
        public readonly Dictionary<object, float> Factors = new();
    }

    private readonly IAudioSessionService _audio;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _clock;
    private bool _hadWaiting;

    public DuckLedger(IAudioSessionService audio, Func<long>? clock = null)
    {
        _audio = audio;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>True while some app is waited for (no session at the release): the app should then watch session changes and call <see cref="RetryPending"/>.</summary>
    public bool HasWaiting => _entries.Values.Any(e => e.WaitUntil is not null);

    /// <summary>Raised (UI thread) when <see cref="HasWaiting"/> changes.</summary>
    public event Action? WaitingChanged;

    private void CheckWaiting()
    {
        var now = HasWaiting;
        if (now == _hadWaiting) return;
        _hadWaiting = now;
        WaitingChanged?.Invoke();
    }

    public bool Holds(object owner, string exe) => _entries.TryGetValue(exe, out var e) && e.Factors.ContainsKey(owner);

    /// <summary>The level currently applied to an app (what the sliders should show), or null when it is not lowered.</summary>
    public float? Applied(string exe) => _entries.TryGetValue(exe, out var e) ? e.Applied : null;

    /// <summary>The apps lowered by <paramref name="owner"/> with the level they come back to.</summary>
    public Dictionary<string, float> OriginalsOf(object owner) =>
        _entries.Where(kv => kv.Value.Factors.ContainsKey(owner)).ToDictionary(kv => kv.Key, kv => kv.Value.Original, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the app's level is no longer the one we applied: the user moved the slider.</summary>
    public bool UserMoved(string exe, float current) => _entries.TryGetValue(exe, out var e) && Math.Abs(current - e.Applied) > Tolerance;

    /// <summary>Forgets an app without touching its level (the user took over).</summary>
    public void Drop(string exe) => _entries.Remove(exe);

    /// <summary>Lowers an app to <paramref name="factor"/> of its level for this owner. False when the app is not playing.</summary>
    public bool Lower(object owner, string exe, float factor)
    {
        if (_entries.TryGetValue(exe, out var entry))
        {
            entry.Factors[owner] = factor;
            entry.Pending = false;
            entry.WaitUntil = null;
            Apply(exe, entry);
            CheckWaiting();
            return true;
        }
        if (_audio.GetVolume(exe) is not { } current) return false;
        if (!_audio.SetVolume(exe, current * factor)) return false;
        entry = new Entry { Original = current };
        entry.Factors[owner] = factor;
        entry.Applied = _audio.GetVolume(exe) ?? current * factor;
        _entries[exe] = entry;
        return true;
    }

    /// <summary>The owner lets go: the app goes to what the remaining owners ask for, or back to the original when none is left.</summary>
    public void Release(object owner, string exe)
    {
        if (!_entries.TryGetValue(exe, out var entry) || !entry.Factors.Remove(owner)) return;
        if (entry.Factors.Count == 0) Finish(exe, entry);
        else Apply(exe, entry);
        CheckWaiting();
    }

    /// <summary>Tries again the restores that failed while their app was still running (a no-op when there are none).</summary>
    public void RetryPending()
    {
        foreach (var (exe, entry) in _entries.Where(kv => (kv.Value.Pending || kv.Value.WaitUntil is not null) && kv.Value.Factors.Count == 0).ToList()) Finish(exe, entry);
        CheckWaiting();
    }

    /// <summary>The last owner is gone: back to the original, unless the user moved the slider or the app is gone.</summary>
    private void Finish(string exe, Entry entry)
    {
        if (_audio.GetVolume(exe) is not { } current)
        {
            // No session now: Windows remembers the lowered level for the app, so wait (bounded) for it to come back.
            var now = _clock();
            if (entry.WaitUntil is not { } until) entry.WaitUntil = now + WaitMs;
            else if (now > until) _entries.Remove(exe);
            return;
        }
        if (Math.Abs(current - entry.Applied) > Tolerance) { _entries.Remove(exe); return; } // the user moved it: their level stays
        if (_audio.SetVolume(exe, entry.Original) || _audio.SetVolume(exe, entry.Original)) _entries.Remove(exe);
        else entry.Pending = true; // the app is still there and the write failed twice: keep the entry for a later retry
    }

    private void Apply(string exe, Entry entry)
    {
        if (_audio.GetVolume(exe) is { } now && Math.Abs(now - entry.Applied) > Tolerance) { _entries.Remove(exe); return; } // the user took over
        var target = entry.Original * entry.Factors.Values.Min();
        if (Math.Abs(target - entry.Applied) <= 0.0005f) return;
        if (_audio.SetVolume(exe, target)) entry.Applied = _audio.GetVolume(exe) ?? target;
    }
}

/// <summary>
/// Lowers the "ducked" apps while a trigger is loud and puts them back afterwards. The trigger is the peak level of the
/// voice apps (automatic ducking) or of a single source such as the microphone (streamer mode, through
/// <c>peakSource</c>); it must be above the threshold for <see cref="DuckingSettings.AttackMs"/> before lowering starts and
/// gone for <see cref="DuckingSettings.ReleaseMs"/> before the levels come back (no pumping on pauses between words). The
/// levels restored are exactly the ones found. An app whose slider the user moves while lowered is let go: its level stays
/// as the user set it. Driven by <see cref="Tick"/> (a timer that runs only while something can trigger).
/// </summary>
public sealed class DuckingEngine
{
    private readonly IAudioSessionService _audio;
    private readonly Func<DuckingSettings> _settings;
    private readonly DuckLedger _ledger;
    private readonly Func<float?>? _peakSource;
    private readonly Func<bool>? _blocked;
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _letGo = new(StringComparer.OrdinalIgnoreCase);
    private long? _loudSince, _quietSince;

    /// <param name="ledger">Shared with the other engines so that levels always come back; a private one when null.</param>
    /// <param name="blocked">True while the engine must not touch anything (competitive mode): a late tick then restores what is lowered and does nothing else.</param>
    /// <param name="peakSource">The trigger level (0..1, null = unavailable); when null the voice apps of the list are used.</param>
    public DuckingEngine(IAudioSessionService audio, Func<DuckingSettings> settings, DuckLedger? ledger = null, Func<float?>? peakSource = null, Func<bool>? blocked = null)
    {
        _blocked = blocked;
        _audio = audio;
        _settings = settings;
        _ledger = ledger ?? new DuckLedger(audio);
        _peakSource = peakSource;
    }

    /// <summary>True between lowering and restoring.</summary>
    public bool Active { get; private set; }

    /// <summary>Apps that are lowered by this engine right now, with the level they will be restored to (for tests and diagnostics).</summary>
    public IReadOnlyDictionary<string, float> Originals => _ledger.OriginalsOf(this);

    /// <summary>One look at the trigger level at time <paramref name="nowMs"/> (any steady clock, in milliseconds).</summary>
    public void Tick(long nowMs)
    {
        if (_blocked?.Invoke() == true) { Restore(); return; }
        var cfg = _settings();
        var threshold = cfg.ThresholdPercent / 100f;
        var loud = false;
        if (_peakSource is not null) loud = _peakSource() is { } mic && mic >= threshold;
        else
            foreach (var app in cfg.AppList)
                if (app.Voice && _audio.GetPeak(app.ExeName) is { } peak && peak >= threshold) { loud = true; break; }

        if (loud) { _quietSince = null; _loudSince ??= nowMs; }
        else { _loudSince = null; _quietSince ??= nowMs; }

        if (!Active)
        {
            if (loud && nowMs - _loudSince >= cfg.AttackMs) { Active = true; Maintain(cfg); }
            return;
        }
        if (!loud && nowMs - _quietSince >= cfg.ReleaseMs) { Restore(); return; }
        Maintain(cfg);
    }

    /// <summary>Lowers apps that were not lowered yet (they may have started meanwhile) and lets go of the ones the user moved.</summary>
    private void Maintain(DuckingSettings cfg)
    {
        var factor = cfg.DuckToPercent / 100f;
        foreach (var app in cfg.AppList)
        {
            if (!app.Ducked || app.Voice || _letGo.Contains(app.ExeName)) continue;
            var current = _audio.GetVolume(app.ExeName);
            if (_held.Contains(app.ExeName))
            {
                // Another engine may have let go because the user moved the slider: do the same.
                if (!_ledger.Holds(this, app.ExeName)) { _held.Remove(app.ExeName); _letGo.Add(app.ExeName); }
                else if (current is { } c && _ledger.UserMoved(app.ExeName, c)) { _ledger.Drop(app.ExeName); _held.Remove(app.ExeName); _letGo.Add(app.ExeName); }
                continue;
            }
            if (current is null) continue; // not playing
            if (_ledger.Lower(this, app.ExeName, factor)) _held.Add(app.ExeName);
        }
    }

    /// <summary>Puts the lowered apps back at once (the trigger stopped, or the mode is being turned off). No-op when not active.</summary>
    public void Restore()
    {
        foreach (var exe in _held) _ledger.Release(this, exe);
        _held.Clear();
        _letGo.Clear();
        _ledger.RetryPending();
        Active = false;
        _loudSince = _quietSince = null;
    }
}
