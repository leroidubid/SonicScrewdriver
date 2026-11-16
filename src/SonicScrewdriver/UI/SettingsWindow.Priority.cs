using System.Windows;
using System.Windows.Controls;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

/// <summary>The output device priority card: an ordered list (up / down / remove) of devices.</summary>
public partial class SettingsWindow
{
    private readonly List<DeviceRef> _priority = new();

    private void InitPriority(AppSettings current)
    {
        PriorityBox.IsChecked = current.DevicePriorityEnabled;
        _priority.AddRange(current.DevicePriority);
        _shortcutOnly.Add(PriorityBox);
        _shortcutOnly.Add(PriorityHint);
        _shortcutOnly.Add(PriorityPanel);
        _shortcutOnly.Add(PriorityAddRow);
        RebuildPriority();
    }

    private void ReadPriority(AppSettings s)
    {
        s.DevicePriorityEnabled = PriorityBox.IsChecked == true;
        s.DevicePriority = _priority.ToList();
    }

    private void AddPriority_Click(object sender, RoutedEventArgs e)
    {
        if (PriorityChoice.SelectedItem is DeviceChoice choice) _priority.Add(new DeviceRef(choice.Id, choice.Label));
        RebuildPriority();
    }

    private void RebuildPriority()
    {
        PriorityPanel.Children.Clear();
        var plugged = _audioService.GetDevices().Select(d => d.Id).ToHashSet();
        for (var i = 0; i < _priority.Count; i++)
        {
            var index = i;
            var device = _priority[i];
            var name = new TextBlock
            {
                Text = $"{i + 1}.  {(device.Name.Length > 0 ? device.Name : device.Id)}{(plugged.Contains(device.Id) ? "" : "  (not plugged in)")}",
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Button Glyph(string glyph, string tip, bool enabled, Action click)
            {
                var b = new Button
                {
                    Content = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), ToolTip = tip, IsEnabled = enabled,
                    Width = 32, Height = 32, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), Style = (Style)FindResource("FlatButton"),
                };
                b.Click += (_, _) => click();
                return b;
            }
            var up = Glyph("\uE70E", "Move up", index > 0, () => Move(index, -1));
            var down = Glyph("\uE70D", "Move down", index < _priority.Count - 1, () => Move(index, 1));
            var remove = Glyph("\uE74D", "Remove", true, () => { _priority.RemoveAt(index); RebuildPriority(); });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var parts = new FrameworkElement[] { name, up, down, remove };
            for (var c = 0; c < parts.Length; c++) { Grid.SetColumn(parts[c], c); grid.Children.Add(parts[c]); }
            PriorityPanel.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("RowBrush"), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 0, 0, 6), Child = grid,
            });
        }
        // Devices that can still be added: plugged in and not in the list yet.
        PriorityChoice.ItemsSource = _deviceChoices.Where(c => _priority.All(p => p.Id != c.Id)).ToList();
        PriorityChoice.SelectedIndex = PriorityChoice.Items.Count > 0 ? 0 : -1;
    }

    private void Move(int index, int delta)
    {
        var to = index + delta;
        if (to < 0 || to >= _priority.Count) return;
        (_priority[index], _priority[to]) = (_priority[to], _priority[index]);
        RebuildPriority();
    }
}
