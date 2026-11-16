namespace SonicScrewdriver.Audio;

/// <summary>
/// Remembers that the app muted the microphone (shortcut, command line or mixer), so that entering competitive mode,
/// which switches the microphone features and their grey icon off, can unmute it first: nothing may stay muted with no
/// way to undo it. A microphone muted elsewhere (Windows settings, a headset button) is never touched. The mute is not
/// re-applied when the mode ends.
/// </summary>
public sealed class MicMuteGuard
{
    /// <summary>True while the last mute written by the app is still in place (as far as the app knows).</summary>
    public bool MutedByApp { get; private set; }

    /// <summary>The microphone whose mute writes are recorded.</summary>
    public IMicrophone Wrap(IMicrophone inner) => new Recording(inner, this);

    /// <summary>Unmutes the microphone when the app muted it; true when it did. Always forgets the record.</summary>
    public bool ReleaseIfMutedByApp(IMicrophone mic)
    {
        var mine = MutedByApp;
        MutedByApp = false;
        if (!mine || !mic.Available || !mic.Muted) return false;
        mic.Muted = false;
        return true;
    }

    private sealed class Recording : IMicrophone
    {
        private readonly IMicrophone _inner;
        private readonly MicMuteGuard _guard;
        public Recording(IMicrophone inner, MicMuteGuard guard) { _inner = inner; _guard = guard; }
        public bool Available => _inner.Available;
        public string Name => _inner.Name;
        public float Volume { get => _inner.Volume; set => _inner.Volume = value; }
        public bool Muted
        {
            get => _inner.Muted;
            set { _inner.Muted = value; _guard.MutedByApp = value; }
        }
    }
}
