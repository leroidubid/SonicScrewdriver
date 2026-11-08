using SonicScrewdriver.Bindings;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.Tests.Bindings;

public class ShortcutSummaryTests
{
    [Fact]
    public void Lists_each_binding_with_modifiers_in_a_stable_order()
    {
        var lines = ShortcutSummary.Lines(new[]
        {
            new KeyBinding(Modifiers.Ctrl, Target.Foreground),
            new KeyBinding(Modifiers.Shift | Modifiers.Ctrl, Target.Process("spotify")),
            new KeyBinding(Modifiers.Win | Modifiers.Alt, Target.OutputDevice),
        });
        Assert.Equal(new[]
        {
            "Ctrl → Foreground app",
            "Ctrl+Shift → spotify.exe",
            "Alt+Win → Output devices",
        }, lines);
    }

    [Fact]
    public void Tooltip_fits_the_shell_limit()
    {
        var many = Enumerable.Range(0, 20).Select(i => $"Ctrl+Shift → some-long-application-name-{i}.exe").ToList();
        var tip = ShortcutSummary.Tooltip("Headset", many);
        Assert.True(tip.Length <= 127);
        Assert.StartsWith("Output: Headset\n", tip);
        Assert.EndsWith("…", tip);
    }

    [Fact]
    public void Tooltip_without_shortcuts_says_so()
        => Assert.Equal("Output: none\nNo shortcuts", ShortcutSummary.Tooltip(null, Array.Empty<string>()));
}
