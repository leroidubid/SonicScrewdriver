using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SonicScrewdriver.Interop;

namespace SonicScrewdriver.UI;

/// <summary>Non-activating, click-through overlay shown bottom-center for 1.5 s.</summary>
public partial class OsdWindow : Window, IOsd
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);
        };
        SizeChanged += (_, _) => Reposition();
    }

    public void Show(string? exe, string title, string detail)
    {
        var icon = IconCache.Get(exe);
        AppIcon.Source = icon;
        Fallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = title;
        DetailText.Text = detail;
        if (detail.EndsWith(" %", StringComparison.Ordinal) && int.TryParse(detail[..^2], out var pct))
        {
            Level.Value = pct;
            Level.Visibility = Visibility.Visible;
        }
        else
        {
            Level.Visibility = Visibility.Collapsed;
        }

        if (!IsVisible) Show();
        Reposition();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 80;
    }
}
