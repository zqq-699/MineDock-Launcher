using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public sealed class DialogOverlayController
{
    private const double BlurRadius = 42;
    private const double AnimationBlurScale = 0.5;
    private const double SizeAnimationThreshold = 1;

    private static readonly TimeSpan AnimationBlurInterval = TimeSpan.FromMilliseconds(33);
    private static readonly Duration FadeInDuration = TimeSpan.FromMilliseconds(160);
    private static readonly Duration FadeOutDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration SizeTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly Color BlurTintColor = Color.FromArgb(0x8A, 0x25, 0x25, 0x25);

    private readonly Window owner;
    private readonly FrameworkElement sourceLayer;
    private bool isSizeAnimating;
    private EventHandler? blurRenderingHandler;
    private DateTime lastAnimationBlurRefreshUtc;

    public DialogOverlayController(Window owner, FrameworkElement sourceLayer)
    {
        this.owner = owner;
        this.sourceLayer = sourceLayer;
    }

    public bool IsSizeAnimating => isSizeAnimating;

    public void QueueRefresh(DialogHost host, int attempts = 5)
    {
        QueueRefresh(host.SurfaceBorder, host.BlurLayerBorder, attempts);
    }

    public void QueueRefresh(FrameworkElement dialog, Border target, int attempts = 5)
    {
        owner.Dispatcher.BeginInvoke(
            () =>
            {
                owner.UpdateLayout();
                if (!UpdateBlur(dialog, target) && attempts > 0)
                    QueueRefresh(dialog, target, attempts - 1);
            },
            DispatcherPriority.ApplicationIdle);
    }

    public void RefreshNow(DialogHost host)
    {
        RefreshNow(host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void RefreshNow(FrameworkElement dialog, Border target)
    {
        owner.UpdateLayout();
        UpdateBlur(dialog, target);
    }

    public void AnimateSizeChange(DialogHost host, double previousHeight)
    {
        AnimateSizeChange(host.SurfaceBorder, host.BlurLayerBorder, previousHeight);
    }

    public void AnimateSizeChange(Border dialog, Border blurTarget, double previousHeight)
    {
        StopRealtimeBlur();
        dialog.BeginAnimation(FrameworkElement.HeightProperty, null);
        dialog.Height = double.NaN;
        isSizeAnimating = true;

        owner.UpdateLayout();
        var targetHeight = dialog.ActualHeight;

        if (previousHeight <= 0
            || targetHeight <= 0
            || Math.Abs(previousHeight - targetHeight) <= SizeAnimationThreshold)
        {
            dialog.Height = double.NaN;
            isSizeAnimating = false;
            RefreshNow(dialog, blurTarget);
            return;
        }

        dialog.Height = previousHeight;
        owner.UpdateLayout();
        UpdateBlur(dialog, blurTarget, AnimationBlurScale);
        StartRealtimeBlur(dialog, blurTarget);

        var animation = new DoubleAnimation
        {
            From = previousHeight,
            To = targetHeight,
            Duration = SizeTransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            StopRealtimeBlur();
            dialog.Height = double.NaN;
            isSizeAnimating = false;
            RefreshNow(dialog, blurTarget);
        };

        dialog.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    public void Show(DialogHost host)
    {
        Show(host.OverlayRoot, host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void Show(Grid overlay, FrameworkElement dialog, Border blurTarget)
    {
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Visible;
        overlay.Opacity = 0;

        owner.UpdateLayout();
        UpdateBlur(dialog, blurTarget);

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = FadeInDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void Hide(DialogHost host, Action? completed = null)
    {
        Hide(host.OverlayRoot, completed);
    }

    public void Hide(Grid overlay, Action? completed = null)
    {
        var currentOpacity = overlay.Opacity;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);

        overlay.Opacity = currentOpacity;
        if (currentOpacity <= 0)
        {
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = 0,
            Duration = FadeOutDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            overlay.Opacity = 0;
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void Prewarm(DialogHost host)
    {
        Prewarm(host.OverlayRoot, host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void Prewarm(Grid overlay, Border dialog, Border blurTarget)
    {
        var originalVisibility = overlay.Visibility;
        var originalOpacity = overlay.Opacity;

        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Hidden;
        overlay.Opacity = 0;

        dialog.ApplyTemplate();
        blurTarget.ApplyTemplate();
        owner.UpdateLayout();
        UpdateBlur(dialog, blurTarget, AnimationBlurScale);

        overlay.Visibility = originalVisibility;
        overlay.Opacity = originalOpacity;
    }

    private void StartRealtimeBlur(FrameworkElement dialog, Border blurTarget)
    {
        lastAnimationBlurRefreshUtc = DateTime.MinValue;
        blurRenderingHandler = (_, _) =>
        {
            if (!isSizeAnimating || !dialog.IsVisible)
                return;

            var now = DateTime.UtcNow;
            if (now - lastAnimationBlurRefreshUtc < AnimationBlurInterval)
                return;

            lastAnimationBlurRefreshUtc = now;
            UpdateBlur(dialog, blurTarget, AnimationBlurScale);
        };

        CompositionTarget.Rendering += blurRenderingHandler;
    }

    private void StopRealtimeBlur()
    {
        if (blurRenderingHandler is null)
            return;

        CompositionTarget.Rendering -= blurRenderingHandler;
        blurRenderingHandler = null;
    }

    private bool UpdateBlur(FrameworkElement dialog, Border target, double renderScale = 1)
    {
        if (!dialog.IsVisible
            || sourceLayer.ActualWidth <= 0
            || sourceLayer.ActualHeight <= 0
            || dialog.ActualWidth <= 0
            || dialog.ActualHeight <= 0)
            return false;

        var dpi = VisualTreeHelper.GetDpi(owner);
        var scale = Math.Clamp(renderScale, 0.25, 1);
        var renderDpiScaleX = dpi.DpiScaleX * scale;
        var renderDpiScaleY = dpi.DpiScaleY * scale;
        var renderDpi = new DpiScale(renderDpiScaleX, renderDpiScaleY);
        var sourceWidth = Math.Max(1, (int)Math.Ceiling(sourceLayer.ActualWidth * renderDpiScaleX));
        var sourceHeight = Math.Max(1, (int)Math.Ceiling(sourceLayer.ActualHeight * renderDpiScaleY));

        var rendered = new RenderTargetBitmap(
            sourceWidth,
            sourceHeight,
            96 * renderDpiScaleX,
            96 * renderDpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(sourceLayer);

        var topLeft = dialog.TransformToVisual(sourceLayer).Transform(new Point(0, 0));
        var cropX = (int)Math.Round(topLeft.X * renderDpiScaleX);
        var cropY = (int)Math.Round(topLeft.Y * renderDpiScaleY);
        var cropWidth = Math.Max(1, (int)Math.Round(dialog.ActualWidth * renderDpiScaleX));
        var cropHeight = Math.Max(1, (int)Math.Round(dialog.ActualHeight * renderDpiScaleY));

        var targetRect = IntersectWithSource(new Int32Rect(cropX, cropY, cropWidth, cropHeight), sourceWidth, sourceHeight);
        if (targetRect.Width <= 0 || targetRect.Height <= 0)
            return false;

        var blurPaddingX = Math.Max(1, (int)Math.Ceiling(BlurRadius * renderDpiScaleX * 2));
        var blurPaddingY = Math.Max(1, (int)Math.Ceiling(BlurRadius * renderDpiScaleY * 2));
        var expandedRect = IntersectWithSource(
            new Int32Rect(
                targetRect.X - blurPaddingX,
                targetRect.Y - blurPaddingY,
                targetRect.Width + blurPaddingX * 2,
                targetRect.Height + blurPaddingY * 2),
            sourceWidth,
            sourceHeight);

        if (expandedRect.Width <= 0 || expandedRect.Height <= 0)
            return false;

        var expandedCrop = new CroppedBitmap(rendered, expandedRect);
        expandedCrop.Freeze();

        var blurredExpanded = CreateBlurredBitmap(expandedCrop, renderDpi);
        var blurredTarget = new CroppedBitmap(
            blurredExpanded,
            new Int32Rect(
                targetRect.X - expandedRect.X,
                targetRect.Y - expandedRect.Y,
                targetRect.Width,
                targetRect.Height));
        blurredTarget.Freeze();

        var dialogBackground = CreateDialogBackgroundBitmap(blurredTarget, renderDpi);

        var brush = EnsureLocalDialogBrush(target);
        brush.ImageSource = dialogBackground;
        return true;
    }

    private static ImageBrush EnsureLocalDialogBrush(Border target)
    {
        if (target.ReadLocalValue(Border.BackgroundProperty) is ImageBrush localBrush && !localBrush.IsFrozen)
            return localBrush;

        var brush = new ImageBrush
        {
            Stretch = Stretch.Fill
        };
        target.Background = brush;
        return brush;
    }

    private static BitmapSource CreateBlurredBitmap(BitmapSource source, DpiScale dpi)
    {
        var width = source.PixelWidth / dpi.DpiScaleX;
        var height = source.PixelHeight / dpi.DpiScaleY;
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Width = width,
            Height = height,
            SnapsToDevicePixels = true,
            Effect = new BlurEffect
            {
                Radius = BlurRadius,
                RenderingBias = RenderingBias.Performance
            }
        };

        var size = new Size(width, height);
        image.Measure(size);
        image.Arrange(new Rect(size));
        image.UpdateLayout();

        var blurred = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        blurred.Render(image);
        blurred.Freeze();
        return blurred;
    }

    private static BitmapSource CreateDialogBackgroundBitmap(BitmapSource blurredSource, DpiScale dpi)
    {
        var width = blurredSource.PixelWidth / dpi.DpiScaleX;
        var height = blurredSource.PixelHeight / dpi.DpiScaleY;
        var rect = new Rect(0, 0, width, height);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), null, rect);
            context.DrawImage(blurredSource, rect);
            context.DrawRectangle(new SolidColorBrush(BlurTintColor), null, rect);
        }

        var bitmap = new RenderTargetBitmap(
            blurredSource.PixelWidth,
            blurredSource.PixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Int32Rect IntersectWithSource(Int32Rect rect, int sourceWidth, int sourceHeight)
    {
        var x = Math.Max(0, rect.X);
        var y = Math.Max(0, rect.Y);
        var right = Math.Min(sourceWidth, rect.X + rect.Width);
        var bottom = Math.Min(sourceHeight, rect.Y + rect.Height);

        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
