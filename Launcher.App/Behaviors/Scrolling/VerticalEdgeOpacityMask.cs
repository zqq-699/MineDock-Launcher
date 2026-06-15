using System.Windows;
using System.Windows.Media;

namespace Launcher.App.Behaviors;

public static class VerticalEdgeOpacityMask
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(VerticalEdgeOpacityMask),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty TopFadeLengthProperty =
        DependencyProperty.RegisterAttached(
            "TopFadeLength",
            typeof(double),
            typeof(VerticalEdgeOpacityMask),
            new PropertyMetadata(0d, OnFadePropertyChanged));

    public static readonly DependencyProperty BottomFadeLengthProperty =
        DependencyProperty.RegisterAttached(
            "BottomFadeLength",
            typeof(double),
            typeof(VerticalEdgeOpacityMask),
            new PropertyMetadata(0d, OnFadePropertyChanged));

    public static readonly DependencyProperty MinimumOpacityProperty =
        DependencyProperty.RegisterAttached(
            "MinimumOpacity",
            typeof(double),
            typeof(VerticalEdgeOpacityMask),
            new PropertyMetadata(0d, OnFadePropertyChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetTopFadeLength(DependencyObject element) => (double)element.GetValue(TopFadeLengthProperty);

    public static void SetTopFadeLength(DependencyObject element, double value) => element.SetValue(TopFadeLengthProperty, value);

    public static double GetBottomFadeLength(DependencyObject element) => (double)element.GetValue(BottomFadeLengthProperty);

    public static void SetBottomFadeLength(DependencyObject element, double value) => element.SetValue(BottomFadeLengthProperty, value);

    public static double GetMinimumOpacity(DependencyObject element) => (double)element.GetValue(MinimumOpacityProperty);

    public static void SetMinimumOpacity(DependencyObject element, double value) => element.SetValue(MinimumOpacityProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        element.SizeChanged -= Element_SizeChanged;

        if ((bool)e.NewValue)
        {
            element.SizeChanged += Element_SizeChanged;
            ApplyMask(element);
            return;
        }

        element.OpacityMask = null;
    }

    private static void OnFadePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element && GetIsEnabled(element))
            ApplyMask(element);
    }

    private static void Element_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
            ApplyMask(element);
    }

    private static void ApplyMask(FrameworkElement element)
    {
        var height = element.ActualHeight;
        if (height <= 0)
        {
            element.OpacityMask = null;
            return;
        }

        var topLength = Math.Clamp(GetTopFadeLength(element), 0d, height);
        var bottomLength = Math.Clamp(GetBottomFadeLength(element), 0d, height);
        var totalFadeLength = topLength + bottomLength;
        if (totalFadeLength > height && totalFadeLength > 0)
        {
            var scale = height / totalFadeLength;
            topLength *= scale;
            bottomLength *= scale;
        }

        if (topLength <= 0 && bottomLength <= 0)
        {
            element.OpacityMask = null;
            return;
        }

        var minimumOpacity = Math.Clamp(GetMinimumOpacity(element), 0d, 1d);
        var topOffset = topLength / height;
        var bottomOffset = 1d - bottomLength / height;
        var edgeColor = Color.FromArgb(ToByte(minimumOpacity), 0, 0, 0);
        var solidColor = Color.FromArgb(byte.MaxValue, 0, 0, 0);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(topLength > 0 ? edgeColor : solidColor, 0));
        brush.GradientStops.Add(new GradientStop(solidColor, topOffset));
        brush.GradientStops.Add(new GradientStop(solidColor, bottomOffset));
        brush.GradientStops.Add(new GradientStop(bottomLength > 0 ? edgeColor : solidColor, 1));
        brush.Freeze();

        element.OpacityMask = brush;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(value * byte.MaxValue);
    }
}
