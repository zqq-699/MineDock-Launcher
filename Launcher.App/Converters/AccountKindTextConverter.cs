using System.Globalization;
using System.Windows.Data;
using Launcher.App.Resources;

namespace Launcher.App.Converters;

public sealed class AccountKindTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isOffline
            ? isOffline ? Strings.Account_TypeOfflineTitle : Strings.Account_TypeMicrosoftTitle
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
