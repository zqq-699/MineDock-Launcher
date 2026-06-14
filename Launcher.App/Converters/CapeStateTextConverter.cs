using System.Globalization;
using System.Windows.Data;
using Launcher.App.Resources;
using Launcher.Application.Accounts;

namespace Launcher.App.Converters;

public sealed class CapeStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AccountCapeOption cape)
            return string.Empty;

        if (cape.IsNone)
            return Strings.Cape_NoneState;

        return cape.IsActive ? Strings.Cape_ActiveState : Strings.Cape_AvailableState;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
