using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class PageTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Account" => "\u8d26\u6237",
            "Home" => "\u4e3b\u9875",
            "Download" => "\u6e38\u620f\u4e0b\u8f7d",
            "GameSettings" => "\u6e38\u620f\u8bbe\u7f6e",
            "Resources" => "\u8d44\u6e90\u4e2d\u5fc3",
            "Settings" => "\u8bbe\u7f6e",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
