using System.Windows;

namespace SonicScrewdriver.UI;

/// <summary>
/// Where a popup (tray menu, mixer) goes: inside the work area of the monitor the tray icon (the cursor) is on, next to the
/// taskbar. Inputs are pixels (cursor and work area as Windows reports them); the result is in device-independent units.
/// </summary>
public static class PopupPlacement
{
    /// <param name="cursor">Cursor position in pixels.</param>
    /// <param name="workArea">Work area (pixels) of the monitor the cursor is on.</param>
    /// <param name="size">Popup size in device-independent units.</param>
    /// <param name="scaleX">Pixels per device-independent unit (1.5 at 150 %).</param>
    /// <param name="cornerWhenInside">With the cursor inside the work area (not on the taskbar): the bottom right corner (mixer) instead of just above the cursor (menu).</param>
    public static Point Place(Point cursor, Rect workArea, Size size, double scaleX, double scaleY, double gap, bool cornerWhenInside)
    {
        Point P(Point px) => new(px.X / scaleX, px.Y / scaleY);
        var c = P(cursor);
        var area = new Rect(P(workArea.TopLeft), P(workArea.BottomRight));

        double left, top;
        if (cursor.X >= workArea.Right) { left = area.Right - size.Width - gap; top = c.Y - size.Height / 2; }   // taskbar on the right
        else if (cursor.X < workArea.Left) { left = area.Left + gap; top = c.Y - size.Height / 2; }              // taskbar on the left
        else
        {
            left = c.X - size.Width / 2;
            if (cursor.Y >= workArea.Bottom) top = area.Bottom - size.Height - gap;                              // taskbar at the bottom
            else if (cursor.Y < workArea.Top) top = area.Top + gap;                                              // taskbar at the top
            else if (cornerWhenInside) { left = area.Right - size.Width - gap; top = area.Bottom - size.Height - gap; }
            else top = c.Y - size.Height - gap;                                                                  // overflow flyout: just above the cursor
        }
        return new Point(
            Math.Clamp(left, area.Left + gap, Math.Max(area.Left + gap, area.Right - size.Width - gap)),
            Math.Clamp(top, area.Top + gap, Math.Max(area.Top + gap, area.Bottom - size.Height - gap)));
    }
}
