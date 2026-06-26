using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.App.Controls;

internal static class IconSourceImageLoader
{
    public static ImageSource? TryLoad(object? source)
    {
        if (source is ImageSource imageSource)
            return imageSource;

        if (source is not string text || string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var uri = CreateIconUri(text.Trim());
            var image = new BitmapImage();
            image.BeginInit();
            if (ShouldLoadImmediately(uri))
            {
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            }

            image.UriSource = uri;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Uri CreateIconUri(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        if (source.StartsWith("/Launcher.App;component/", StringComparison.OrdinalIgnoreCase))
            return new Uri($"pack://application:,,,{source}", UriKind.Absolute);

        if (source.StartsWith("/", StringComparison.Ordinal))
            return new Uri($"pack://application:,,,/Launcher.App;component{source}", UriKind.Absolute);

        if (Uri.TryCreate(source, UriKind.Relative, out var relativeUri))
            return relativeUri;

        return new Uri(source, UriKind.RelativeOrAbsolute);
    }

    private static bool ShouldLoadImmediately(Uri uri)
    {
        return uri.IsFile
            || string.Equals(uri.Scheme, "pack", StringComparison.OrdinalIgnoreCase);
    }
}
