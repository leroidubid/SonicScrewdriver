using System.Windows;
using System.Windows.Controls;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.UI;

/// <summary>The remembered routing card: an app and the output device it plays on.</summary>
public partial class SettingsWindow
{
    private sealed record DeviceChoice(string Id, string Label);
    private sealed record RoutingRow(ComboBox Exe, ComboBox Device);
    private readonly List<RoutingRow> _routingRows = new();
    private List<DeviceChoice> _deviceChoices = new();

    private void InitRouting(AppSettings current)
    {
        _shortcutOnly.Add(RoutingHint);
        _shortcutOnly.Add(RoutingPanel);
        _shortcutOnly.Add(AddRoutingButton);
        foreach (var r in current.RoutingRules) AddRoutingRow(r);
    }

    private void AddRouting_Click(object sender, RoutedEventArgs e) => AddRoutingRow(null);

    private void AddRoutingRow(RoutingRule? rule)
    {
        // A device that is not plugged in stays in the list (by its stored name) so the rule is not lost on Save.
        var choices = _deviceChoices.ToList();
        if (rule is not null && choices.All(c => c.Id != rule.DeviceId))
            choices.Add(new DeviceChoice(rule.DeviceId, (rule.DeviceName.Length > 0 ? rule.DeviceName : rule.DeviceId) + " (not plugged in)"));
        var exe = new ComboBox { IsEditable = true, Height = 32, MinWidth = 150, ItemsSource = AutoChoices, Text = rule?.ExeName ?? "", ToolTip = "Executable name, e.g. discord.exe" };
        var device = new ComboBox
        {
            Height = 32, MinWidth = 200, Margin = new Thickness(6, 0, 6, 0), ItemsSource = choices, DisplayMemberPath = "Label", SelectedValuePath = "Id",
            SelectedValue = rule?.DeviceId, ToolTip = "Output device",
        };
        var row = new RoutingRow(exe, device);
        RemovableRow(RoutingPanel, () => _routingRows.Remove(row), exe, device);
        _routingRows.Add(row);
    }

    private List<RoutingRule> ReadRouting() => _routingRows.Select(r =>
    {
        var choice = r.Device.SelectedItem as DeviceChoice;
        return new RoutingRule(BindingRowViewModel.ExeFromLabel(r.Exe.Text ?? ""), choice?.Id ?? "", (choice?.Label ?? "").Replace(" (not plugged in)", ""));
    }).ToList();
}
