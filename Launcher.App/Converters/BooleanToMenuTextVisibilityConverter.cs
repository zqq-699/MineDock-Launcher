using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class BooleanToMenuTextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
