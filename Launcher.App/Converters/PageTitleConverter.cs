using System.Globalization;
using System.Windows.Data;
using Launcher.App.Resources;

namespace Launcher.App.Converters;

public sealed class PageTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Account" => Strings.Page_Account,
            "Home" => Strings.Page_Home,
            "Download" => Strings.Page_Download,
            "Install" => Strings.Page_Install,
            "GameSettings" => Strings.Page_GameSettings,
            "Resources" => Strings.Page_Resources,
            "Settings" => Strings.Page_Settings,
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
