using SonicScrewdriver.Input;

namespace SonicScrewdriver.Bindings;

public static class VolumeMath
{
    /// <summary>Applies a volume step (scalar 0..1), clamped. Non-volume keys return <paramref name="current"/>.</summary>
    public static float Apply(float current, MediaKey key, int stepPercent)
    {
        var step = stepPercent / 100f;
        var next = key switch
        {
            MediaKey.VolumeUp => current + step,
            MediaKey.VolumeDown => current - step,
            _ => current,
        };
        // Round to the step grid's precision to avoid 0.49999 drift.
        return Math.Clamp(MathF.Round(next, 4), 0f, 1f);
    }
}
