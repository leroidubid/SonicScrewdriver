using SonicScrewdriver.Input;

namespace SonicScrewdriver.Tests.Input;

public class KeyLatchTests
{
    private const int VolDown = 0xAE;
    private const int VolUp = 0xAF;

    [Fact]
    public void First_down_decides_and_repeats_keep_the_decision_even_if_modifiers_change()
    {
        var latch = new KeyLatch<string>();
        Assert.Equal("shift-binding", latch.OnDown(VolDown, () => "shift-binding", out var repeat1));
        Assert.False(repeat1);
        // Shift released mid-hold: the resolver would now return null, but the repeat stays latched.
        Assert.Equal("shift-binding", latch.OnDown(VolDown, () => null, out var repeat2));
        Assert.True(repeat2);
        Assert.True(latch.OnUp(VolDown));   // key-up swallowed
        Assert.False(latch.OnUp(VolDown));  // only once
    }

    [Fact]
    public void Passed_through_first_down_keeps_passing_even_if_a_modifier_is_pressed_mid_hold()
    {
        var latch = new KeyLatch<string>();
        Assert.Null(latch.OnDown(VolDown, () => null, out _));
        Assert.Null(latch.OnDown(VolDown, () => "shift-binding", out var repeat));
        Assert.True(repeat);
        Assert.False(latch.OnUp(VolDown));  // key-up passes too, so Windows sees a matched pair
    }

    [Fact]
    public void Keys_are_latched_independently()
    {
        var latch = new KeyLatch<string>();
        latch.OnDown(VolDown, () => "a", out _);
        Assert.Null(latch.OnDown(VolUp, () => null, out _));
        Assert.False(latch.OnUp(VolUp));
        Assert.True(latch.OnUp(VolDown));
    }

    [Fact]
    public void Up_without_down_passes()
        => Assert.False(new KeyLatch<string>().OnUp(VolDown));
}
