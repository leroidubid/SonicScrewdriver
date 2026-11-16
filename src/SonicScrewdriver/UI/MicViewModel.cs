using SonicScrewdriver.Audio;

namespace SonicScrewdriver.UI;

/// <summary>The microphone row of the mixer: volume (0-100) and mute, written to the microphone as the user moves them.</summary>
public sealed class MicViewModel : ObservableObject
{
    private readonly IMicrophone _mic;
    private double _volume;
    private bool _muted;
    private string _name;

    public MicViewModel(IMicrophone mic)
    {
        _mic = mic;
        _name = mic.Name;
        _volume = Math.Round(mic.Volume * 100);
        _muted = mic.Muted;
    }

    public string Name { get => _name; private set { if (value != _name) { _name = value; Raise(); } } }

    public double Volume
    {
        get => _volume;
        set
        {
            var v = Math.Round(Math.Clamp(value, 0, 100));
            if (v == _volume) return;
            _volume = v;
            _mic.Volume = (float)(v / 100);
            Raise();
        }
    }

    /// <summary>True while the slider thumb is held: refreshes must not move it under the cursor.</summary>
    public bool Dragging { get; set; }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (value == _muted) return;
            _muted = value;
            _mic.Muted = value;
            Raise();
        }
    }

    /// <summary>Re-read the microphone without writing back.</summary>
    public void Sync()
    {
        Name = _mic.Name;
        var v = Math.Round(_mic.Volume * 100);
        if (v != _volume && !Dragging) { _volume = v; Raise(nameof(Volume)); }
        var m = _mic.Muted;
        if (m != _muted) { _muted = m; Raise(nameof(Muted)); }
    }
}
