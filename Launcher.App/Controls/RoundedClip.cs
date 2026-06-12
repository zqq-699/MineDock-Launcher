using System.Windows;
using System.Windows.Media;

namespace Launcher.App.Controls;

public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached(
            "Radius",
            typeof(double),
            typeof(RoundedClip),
            new PropertyMetadata(0d, OnRadiusChanged));

    public static double GetRadius(DependencyObject element)
    {
        return (double)element.GetValue(RadiusProperty);
    }

    public static void SetRadius(DependencyObject element, double value)
    {
        element.SetValue(RadiusProperty, value);
    }

    private static void OnRadiusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        element.SizeChanged -= Element_SizeChanged;

        if ((double)e.NewValue <= 0)
        {
            element.Clip = null;
            return;
        }

        element.SizeChanged += Element_SizeChanged;
        ApplyClip(element);
    }

    private static void Element_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
            ApplyClip(element);
    }

    private static void ApplyClip(FrameworkElement element)
    {
        var radius = GetRadius(element);
        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }
}
