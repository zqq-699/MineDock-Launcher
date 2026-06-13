using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Launcher.App.Animations;

namespace Launcher.App.Controls;

public sealed class NavigationMenuController
{
    private const double CollapsedWidth = 62;
    private const double ExpandedWidth = 210;

    private static readonly TimeSpan WidthAnimationDuration = TimeSpan.FromMilliseconds(360);

    private readonly ColumnDefinition menuColumn;

    public NavigationMenuController(ColumnDefinition menuColumn)
    {
        this.menuColumn = menuColumn;
    }

    public void SetExpanded(bool isExpanded)
    {
        menuColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        menuColumn.Width = GetWidth(isExpanded);
    }

    public void AnimateExpanded(bool isExpanded)
    {
        var targetWidth = GetWidth(isExpanded);
        var animation = new GridLengthAnimation
        {
            From = new GridLength(menuColumn.ActualWidth),
            To = targetWidth,
            Duration = WidthAnimationDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) =>
        {
            menuColumn.Width = targetWidth;
        };

        menuColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private static GridLength GetWidth(bool isExpanded)
    {
        return new GridLength(isExpanded ? ExpandedWidth : CollapsedWidth);
    }
}
