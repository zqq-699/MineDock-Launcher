using System.Windows;
using System.Windows.Media;

namespace Launcher.App.Controls;

public static class VisualTreeSearch
{
    public static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild && predicate(typedChild))
                return typedChild;

            var match = FindDescendant(child, predicate);
            if (match is not null)
                return match;
        }

        return null;
    }
}
