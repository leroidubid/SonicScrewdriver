using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SonicScrewdriver.Interop;

namespace SonicScrewdriver.UI;

/// <summary>
/// Right-click menu of the tray icon, in the same look as the mixer flyout. Closes when it loses the focus (a click
/// anywhere else) or on Escape; Up/Down/Tab move between the entries and Enter or Space runs one.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private const double Gap = 12;
    private DateTime _hiddenAt = DateTime.MinValue;

    public event EventHandler? SettingsRequested;
    public event EventHandler? CompetitiveToggleRequested;
    public event EventHandler? QuitRequested;
    /// <summary>A profile entry was chosen; the argument is the profile name.</summary>
    public event EventHandler<string>? ProfileRequested;

    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => HideMenu();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HideMenu();
                e.Handled = true;
                break;
            case Key.Down:
                Step(FocusNavigationDirection.Next);
                e.Handled = true;
                break;
            case Key.Up:
                Step(FocusNavigationDirection.Previous);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Moves from the focused entry (from the first one when none is focused yet).</summary>
    private void Step(FocusNavigationDirection direction) =>
        (Keyboard.FocusedElement as FrameworkElement ?? this).MoveFocus(new TraversalRequest(direction));

    /// <summary>
    /// Opens the menu near <paramref name="cursor"/> (screen pixels), inside the work area so it never lies behind
    /// the taskbar: above a bottom taskbar, below a top one, beside a side one. A second right-click on the icon just
    /// after the menu closed itself does not reopen it (the click that closed it was that same click).
    /// </summary>
    public void ShowAt(Point cursor, Rect workArea, string device, IReadOnlyList<string> lines, bool competitive, string? hotkey, IReadOnlyList<string> profiles)
    {
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 400) return;

        DeviceText.Text = "Output: " + device;
        Lines.Children.Clear();
        foreach (var line in lines)
            Lines.Children.Add(new TextBlock { Text = line, Foreground = (Brush)FindResource("SubtleBrush"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        Lines.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ProfilesPanel.Children.Clear();
        foreach (var name in profiles)
        {
            var item = new Button { Style = (Style)FindResource("MenuItemButton"), Tag = name };
            System.Windows.Automation.AutomationProperties.SetName(item, "Profile " + name);
            var row = new DockPanel();
            row.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 22, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
            item.Content = row;
            item.Click += (_, _) => { HideMenu(); ProfileRequested?.Invoke(this, name); };
            ProfilesPanel.Children.Add(item);
        }
        ProfilesPanel.Visibility = profiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CompetitiveGlyph.Opacity = competitive ? 1 : 0;
        CompetitiveGlyph.Foreground = competitive ? (Brush)FindResource("ErrorBrush") : (Brush)FindResource("FgBrush");
        CompetitiveHint.Text = hotkey ?? "";

        // A window that is not shown yet reports no desired size: measure its content at the window width.
        var content = (FrameworkElement)Content;
        content.Measure(new Size(Width, double.PositiveInfinity));
        var size = new Size(Width, content.DesiredSize.Height);
        var scale = VisualTreeHelper.GetDpi(this);
        var at = PopupPlacement.Place(cursor, workArea, size, scale.DpiScaleX, scale.DpiScaleY, Gap, cornerWhenInside: false);
        Left = at.X;
        Top = at.Y;

        Show();
        Activate();
        // The tray click is user input, so Windows lets the menu take the foreground; without it a click
        // elsewhere would not deactivate (and so close) the menu.
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Focus();
    }

    private void HideMenu()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => Run(SettingsRequested);
    private void Competitive_Click(object sender, RoutedEventArgs e) => Run(CompetitiveToggleRequested);
    private void Quit_Click(object sender, RoutedEventArgs e) => Run(QuitRequested);

    private void Run(EventHandler? handler)
    {
        HideMenu();
        handler?.Invoke(this, EventArgs.Empty);
    }
}
