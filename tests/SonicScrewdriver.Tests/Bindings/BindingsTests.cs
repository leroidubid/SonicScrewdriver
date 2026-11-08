using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.Tests.Bindings;

public class TargetTests
{
    [Theory]
    [InlineData("Spotify")]
    [InlineData("SPOTIFY.EXE")]
    [InlineData(" spotify.exe ")]
    public void Process_normalizes(string raw)
        => Assert.Equal("spotify.exe", Target.Process(raw).ExeName);

    [Fact]
    public void Foreground_has_no_exe()
    {
        Assert.Equal(TargetKind.Foreground, Target.Foreground.Kind);
        Assert.Null(Target.Foreground.ExeName);
    }
}

public class BindingResolverTests
{
    private readonly KeyBinding _ctrl = new(Modifiers.Ctrl, Target.Foreground);
    private readonly KeyBinding _shift = new(Modifiers.Shift, Target.Process("spotify"));

    [Fact]
    public void Resolves_exact_modifiers()
    {
        var r = new BindingResolver(new[] { _ctrl, _shift });
        Assert.Same(_ctrl, r.Resolve(Modifiers.Ctrl));
        Assert.Same(_shift, r.Resolve(Modifiers.Shift));
    }

    [Fact]
    public void Superset_of_modifiers_does_not_match()
        => Assert.Null(new BindingResolver(new[] { _ctrl, _shift }).Resolve(Modifiers.Ctrl | Modifiers.Shift));

    [Fact]
    public void No_modifier_never_matches()
        => Assert.Null(new BindingResolver(new[] { _ctrl }).Resolve(Modifiers.None));
}

public class VolumeMathTests
{
    [Theory]
    [InlineData(0.50f, MediaKey.VolumeUp, 5, 0.55f)]
    [InlineData(0.98f, MediaKey.VolumeUp, 5, 1.00f)]
    [InlineData(0.02f, MediaKey.VolumeDown, 5, 0.00f)]
    [InlineData(0.50f, MediaKey.VolumeDown, 2, 0.48f)]
    [InlineData(0.40f, MediaKey.Mute, 5, 0.40f)]
    public void Apply(float current, MediaKey key, int step, float expected)
        => Assert.Equal(expected, VolumeMath.Apply(current, key, step), 3);
}
