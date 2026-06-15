using System.Globalization;
using System.Windows.Data;
using Launcher.App.Resources;

namespace Launcher.App.Converters;

public sealed class UuidTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string uuid && !string.IsNullOrWhiteSpace(uuid)
            ? uuid
            : Strings.Account_NoneValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
