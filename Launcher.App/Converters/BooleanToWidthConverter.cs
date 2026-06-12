using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class BooleanToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var values = (parameter?.ToString() ?? "220;62").Split(';');
        var expanded = values.Length > 0 && double.TryParse(values[0], out var e) ? e : 220;
        var collapsed = values.Length > 1 && double.TryParse(values[1], out var c) ? c : 62;
        return value is true ? expanded : collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
