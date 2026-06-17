using System.Globalization;
using System.Windows.Data;
using Launcher.App.Utilities;
using Launcher.Application.Accounts;

namespace Launcher.App.Converters;

public sealed class CapeDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is AccountCapeOption cape
            ? AccountCapeTextProvider.GetDisplayName(cape)
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
