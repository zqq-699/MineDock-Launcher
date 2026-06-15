using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Launcher.App.Services;

internal static class BlurEffectWarmup
{
    private static bool isWarmed;
    private static readonly object SyncRoot = new();

    public static void EnsureWarmed()
    {
        lock (SyncRoot)
        {
            if (isWarmed)
                return;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), null, new Rect(0, 0, 16, 16));
            }

            var source = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            source.Render(visual);

            var image = new Image
            {
                Width = 16,
                Height = 16,
                Source = source,
                Stretch = Stretch.Fill,
                Effect = new BlurEffect
                {
                    Radius = 12,
                    RenderingBias = RenderingBias.Performance
                }
            };

            var size = new Size(16, 16);
            image.Measure(size);
            image.Arrange(new Rect(size));
            image.UpdateLayout();

            var blurred = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            blurred.Render(image);

            isWarmed = true;
        }
    }
}
