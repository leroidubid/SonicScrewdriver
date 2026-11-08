using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SonicScrewdriver.UI;

/// <summary>Attached behavior: the mouse wheel over an element moves the given slider by 2 per notch.</summary>
public static class WheelVolume
{
    public const double StepPerNotch = 2;

    public static readonly DependencyProperty SliderProperty = DependencyProperty.RegisterAttached(
        "Slider", typeof(Slider), typeof(WheelVolume), new PropertyMetadata(null, OnSliderChanged));

    public static Slider? GetSlider(DependencyObject d) => (Slider?)d.GetValue(SliderProperty);
    public static void SetSlider(DependencyObject d, Slider? value) => d.SetValue(SliderProperty, value);

    private static void OnSliderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is not null) element.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d || GetSlider(d) is not { } slider) return;
        slider.Value = Math.Clamp(slider.Value + e.Delta / 120.0 * StepPerNotch, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }
}
