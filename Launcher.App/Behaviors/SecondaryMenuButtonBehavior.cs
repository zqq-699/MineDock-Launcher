using System.Windows;

namespace Launcher.App.Behaviors;

public static class SecondaryMenuButtonBehavior
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(SecondaryMenuButtonBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SuppressSelectedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "SuppressSelectedBackground",
            typeof(bool),
            typeof(SecondaryMenuButtonBehavior),
            new PropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element)
    {
        return (bool)element.GetValue(IsSelectedProperty);
    }

    public static void SetIsSelected(DependencyObject element, bool value)
    {
        element.SetValue(IsSelectedProperty, value);
    }

    public static bool GetSuppressSelectedBackground(DependencyObject element)
    {
        return (bool)element.GetValue(SuppressSelectedBackgroundProperty);
    }

    public static void SetSuppressSelectedBackground(DependencyObject element, bool value)
    {
        element.SetValue(SuppressSelectedBackgroundProperty, value);
    }
}
