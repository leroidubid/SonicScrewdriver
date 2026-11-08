using SonicScrewdriver.Bindings;
using SonicScrewdriver.UI;

namespace SonicScrewdriver.Tests.Bindings;

public class DeviceCycleTests
{
    private static readonly string[] Active = { "a", "b", "c" };

    [Theory]
    [InlineData("a", 1, "b")]
    [InlineData("c", 1, "a")]   // wraps
    [InlineData("a", -1, "c")]  // wraps backwards
    [InlineData(null, 1, "a")]  // no current default: start of the list
    public void Cycles_through_all_active_devices_when_no_selection(string? current, int direction, string expected)
        => Assert.Equal(expected, DeviceCycle.Next(Active, current, Array.Empty<string>(), direction));

    [Fact]
    public void Skips_devices_not_in_the_selection()
        => Assert.Equal("c", DeviceCycle.Next(Active, "a", new[] { "a", "c" }, 1));

    [Fact]
    public void Current_outside_the_selection_goes_to_first_selected()
        => Assert.Equal("c", DeviceCycle.Next(Active, "b", new[] { "c" }, 1));

    [Fact]
    public void Selected_devices_that_are_unplugged_are_ignored()
        => Assert.Equal("a", DeviceCycle.Next(Active, "a", new[] { "a", "gone" }, 1));

    [Fact]
    public void No_active_device_returns_null()
        => Assert.Null(DeviceCycle.Next(Array.Empty<string>(), null, Array.Empty<string>(), 1));
}

public class BindingRowParsingTests
{
    [Theory]
    [InlineData("Claude (claude.exe)", "claude.exe")]
    [InlineData("Google Chrome (chrome.exe)", "chrome.exe")]
    [InlineData("spotify", "spotify.exe")]
    [InlineData("Foo (bar) (baz.exe)", "baz.exe")]
    public void Target_text_accepts_name_and_exe(string text, string exe)
        => Assert.Equal(exe, new BindingRowViewModel { TargetText = text, Shift = true }.ToBinding().Target.ExeName);

    [Fact]
    public void Output_device_label_maps_to_output_device_target()
        => Assert.Equal(TargetKind.OutputDevice,
            new BindingRowViewModel { TargetText = BindingRowViewModel.OutputDeviceLabel, Alt = true }.ToBinding().Target.Kind);
}
