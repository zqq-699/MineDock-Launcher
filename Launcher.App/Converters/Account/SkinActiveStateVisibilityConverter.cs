using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class SkinActiveStateVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return Visibility.Collapsed;

        var skinId = values[0]?.ToString();
        var activeSkinId = values[1]?.ToString();
        return !string.IsNullOrWhiteSpace(skinId)
            && string.Equals(skinId, activeSkinId, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
