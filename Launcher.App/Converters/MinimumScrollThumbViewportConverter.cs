using System;
using System.Globalization;
using System.Windows.Data;

namespace Launcher.App.Converters;

public sealed class MinimumScrollThumbViewportConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetDouble(values, 0, out var viewportSize)
            || !TryGetDouble(values, 1, out var scrollableSize)
            || !TryGetDouble(values, 2, out var trackLength))
        {
            return Binding.DoNothing;
        }

        if (viewportSize <= 0 || scrollableSize <= 0 || trackLength <= 0)
        {
            return viewportSize;
        }

        var minimumThumbLength = ParseMinimumThumbLength(parameter, culture);
        if (minimumThumbLength <= 0 || trackLength <= minimumThumbLength)
        {
            return viewportSize;
        }

        var currentThumbLength = viewportSize / (scrollableSize + viewportSize) * trackLength;
        if (currentThumbLength >= minimumThumbLength)
        {
            return viewportSize;
        }

        return minimumThumbLength * scrollableSize / (trackLength - minimumThumbLength);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool TryGetDouble(object[] values, int index, out double value)
    {
        value = 0;
        if (values.Length <= index || values[index] is not double candidate)
        {
            return false;
        }

        if (double.IsNaN(candidate) || double.IsInfinity(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static double ParseMinimumThumbLength(object parameter, CultureInfo culture)
    {
        return parameter switch
        {
            double value => value,
            int value => value,
            string text when double.TryParse(text, NumberStyles.Float, culture, out var value) => value,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => 58
        };
    }
}
