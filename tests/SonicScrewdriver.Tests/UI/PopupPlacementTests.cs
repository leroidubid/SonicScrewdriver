using System.Windows;
using SonicScrewdriver.UI;

namespace SonicScrewdriver.Tests.UI;

/// <summary>Second review, finding 7: the mixer opens on the monitor of the tray icon, inside its work area.</summary>
public class PopupPlacementTests
{
    private const double Gap = 12;
    private static readonly Size Mixer = new(360, 500);

    [Fact]
    public void Bottom_taskbar_on_the_primary_monitor_puts_the_popup_at_the_right_edge()
    {
        var work = new Rect(0, 0, 1920, 1040);                  // taskbar 40 px at the bottom
        var p = PopupPlacement.Place(new Point(1880, 1060), work, Mixer, 1, 1, Gap, cornerWhenInside: true);
        Assert.Equal(1920 - 360 - Gap, p.X);
        Assert.Equal(1040 - 500 - Gap, p.Y);
    }

    [Fact]
    public void A_secondary_monitor_to_the_left_gets_the_popup_there_not_on_the_primary()
    {
        var work = new Rect(-1920, 0, 1920, 1040);              // monitor left of the primary: negative coordinates
        var p = PopupPlacement.Place(new Point(-100, 1060), work, Mixer, 1, 1, Gap, cornerWhenInside: true);
        Assert.InRange(p.X, -1920 + Gap, -Gap);
        Assert.Equal(1040 - 500 - Gap, p.Y);
    }

    [Fact]
    public void A_high_dpi_monitor_converts_pixels_to_units()
    {
        var work = new Rect(1920, 0, 2880, 1560);               // 150 % monitor at the right of the primary
        var p = PopupPlacement.Place(new Point(4700, 1580), work, Mixer, 1.5, 1.5, Gap, cornerWhenInside: true);
        Assert.Equal(4800 / 1.5 - 360 - Gap, p.X, 3);           // right edge of that monitor, in units
        Assert.Equal(1560 / 1.5 - 500 - Gap, p.Y, 3);
    }

    [Fact]
    public void Cursor_inside_the_work_area_uses_the_corner_for_the_mixer_and_the_cursor_for_the_menu()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var cursor = new Point(800, 600);
        var corner = PopupPlacement.Place(cursor, work, Mixer, 1, 1, Gap, cornerWhenInside: true);
        var menu = PopupPlacement.Place(cursor, work, new Size(240, 200), 1, 1, Gap, cornerWhenInside: false);
        Assert.Equal(1920 - 360 - Gap, corner.X);
        Assert.Equal(1040 - 500 - Gap, corner.Y);
        Assert.Equal(800 - 120, menu.X);
        Assert.Equal(600 - 200 - Gap, menu.Y);
    }

    [Fact]
    public void Taskbar_on_the_left_and_on_top_keep_the_popup_inside_the_work_area()
    {
        var left = PopupPlacement.Place(new Point(20, 900), new Rect(48, 0, 1872, 1080), Mixer, 1, 1, Gap, true);
        Assert.Equal(48 + Gap, left.X);
        var top = PopupPlacement.Place(new Point(1800, 10), new Rect(0, 48, 1920, 1032), Mixer, 1, 1, Gap, true);
        Assert.Equal(48 + Gap, top.Y);
        Assert.True(top.X + 360 <= 1920 - Gap + 0.001);
    }

    [Fact]
    public void A_popup_larger_than_the_area_stays_anchored_to_its_top_left()
    {
        var p = PopupPlacement.Place(new Point(500, 1060), new Rect(0, 0, 400, 300), new Size(600, 900), 1, 1, Gap, true);
        Assert.Equal(Gap, p.X);
        Assert.Equal(Gap, p.Y);
    }
}
