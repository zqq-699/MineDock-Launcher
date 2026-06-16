using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Launcher.App.Controls;

namespace Launcher.App.Services;

public sealed class DialogOverlayService
{
    private const double BlurRadius = 42;
    private const double StaticBlurScale = 0.65;
    private const double AnimationBlurScale = 0.5;
    private const double SizeAnimationThreshold = 1;

    private static readonly TimeSpan AnimationBlurInterval = TimeSpan.Zero;
    private static readonly Duration FadeInDuration = TimeSpan.FromMilliseconds(160);
    private static readonly Duration FadeOutDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration SizeTransitionDuration = TimeSpan.FromMilliseconds(240);
    private readonly Window owner;
    private readonly FrameworkElement sourceLayer;
    private bool isSizeAnimating;
    private bool isRefreshQueued;
    private int queuedRefreshAttempts;
    private BitmapSource? animationBlurredBitmap;
    private DpiScale animationSourceDpi;
    private EventHandler? blurRenderingHandler;
    private DateTime lastAnimationBlurRefreshUtc;

    public DialogOverlayService(Window owner, FrameworkElement sourceLayer)
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
        queuedRefreshAttempts = Math.Max(queuedRefreshAttempts, attempts);
        if (isRefreshQueued)
            return;

        isRefreshQueued = true;
        owner.Dispatcher.BeginInvoke(
            () =>
            {
                var remainingAttempts = queuedRefreshAttempts;
                queuedRefreshAttempts = 0;
                isRefreshQueued = false;

                if (!TryUpdateBlur(dialog, target, StaticBlurScale) && remainingAttempts > 0)
                    QueueRefresh(dialog, target, remainingAttempts - 1);
            },
            DispatcherPriority.ApplicationIdle);
    }

    public void RefreshNow(DialogHost host)
    {
        RefreshNow(host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void RefreshNow(FrameworkElement dialog, Border target)
    {
        TryUpdateBlur(dialog, target, StaticBlurScale);
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
        dialog.UpdateLayout();
        if (TryCaptureSourceBitmap(AnimationBlurScale, out var animationSource, out var animationDpi))
        {
            animationBlurredBitmap = CreateBlurredBitmap(animationSource, animationDpi);
            animationSourceDpi = animationDpi;
            UpdateBlurFromBlurredSource(dialog, blurTarget, animationBlurredBitmap, animationSourceDpi);
            StartRealtimeBlur(dialog, blurTarget);
        }

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
            QueueRefresh(dialog, blurTarget);
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

        QueueRefresh(dialog, blurTarget);

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
        dialog.UpdateLayout();
        UpdateBlur(dialog, blurTarget, AnimationBlurScale);

        overlay.Visibility = originalVisibility;
        overlay.Opacity = originalOpacity;
    }

    private void StartRealtimeBlur(FrameworkElement dialog, Border blurTarget)
    {
        lastAnimationBlurRefreshUtc = DateTime.MinValue;
        blurRenderingHandler = (_, _) =>
        {
            if (!isSizeAnimating || !dialog.IsVisible || animationBlurredBitmap is null)
                return;

            var now = DateTime.UtcNow;
            if (AnimationBlurInterval > TimeSpan.Zero && now - lastAnimationBlurRefreshUtc < AnimationBlurInterval)
                return;

            lastAnimationBlurRefreshUtc = now;
            UpdateBlurFromBlurredSource(dialog, blurTarget, animationBlurredBitmap, animationSourceDpi);
        };

        CompositionTarget.Rendering += blurRenderingHandler;
    }

    private void StopRealtimeBlur()
    {
        if (blurRenderingHandler is null)
            return;

        CompositionTarget.Rendering -= blurRenderingHandler;
        blurRenderingHandler = null;
        animationBlurredBitmap = null;
    }

    private bool UpdateBlur(FrameworkElement dialog, Border target, double renderScale = 1)
    {
        if (!TryCaptureSourceBitmap(renderScale, out var renderedSource, out var renderDpi))
            return false;

        return UpdateBlur(dialog, target, renderedSource, renderDpi);
    }

    private bool UpdateBlur(FrameworkElement dialog, Border target, BitmapSource renderedSource, DpiScale renderDpi)
    {
        if (!dialog.IsVisible
            || dialog.ActualWidth <= 0
            || dialog.ActualHeight <= 0)
            return false;

        var sourceWidth = renderedSource.PixelWidth;
        var sourceHeight = renderedSource.PixelHeight;
        var renderDpiScaleX = renderDpi.DpiScaleX;
        var renderDpiScaleY = renderDpi.DpiScaleY;
        var targetRect = GetTargetRect(dialog, renderDpiScaleX, renderDpiScaleY, sourceWidth, sourceHeight);
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

        var expandedCrop = new CroppedBitmap(renderedSource, expandedRect);
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

        var brush = EnsureLocalDialogBrush(target);
        brush.ImageSource = blurredTarget;
        brush.Viewbox = new Rect(0, 0, blurredTarget.Width, blurredTarget.Height);
        return true;
    }

    private bool UpdateBlurFromBlurredSource(FrameworkElement dialog, Border target, BitmapSource blurredSource, DpiScale renderDpi)
    {
        if (!dialog.IsVisible
            || dialog.ActualWidth <= 0
            || dialog.ActualHeight <= 0)
            return false;

        var targetRect = GetTargetRect(
            dialog,
            renderDpi.DpiScaleX,
            renderDpi.DpiScaleY,
            blurredSource.PixelWidth,
            blurredSource.PixelHeight);
        if (targetRect.Width <= 0 || targetRect.Height <= 0)
            return false;

        var brush = EnsureLocalDialogBrush(target);
        brush.ImageSource = blurredSource;
        brush.Viewbox = new Rect(
            targetRect.X / renderDpi.DpiScaleX,
            targetRect.Y / renderDpi.DpiScaleY,
            targetRect.Width / renderDpi.DpiScaleX,
            targetRect.Height / renderDpi.DpiScaleY);
        return true;
    }

    private bool TryCaptureSourceBitmap(double renderScale, out BitmapSource renderedSource, out DpiScale renderDpi)
    {
        renderedSource = null!;
        renderDpi = default;

        if (sourceLayer.ActualWidth <= 0 || sourceLayer.ActualHeight <= 0)
            return false;

        var dpi = VisualTreeHelper.GetDpi(sourceLayer);
        var scale = Math.Clamp(renderScale, 0.25, 1);
        var renderDpiScaleX = dpi.DpiScaleX * scale;
        var renderDpiScaleY = dpi.DpiScaleY * scale;
        renderDpi = new DpiScale(renderDpiScaleX, renderDpiScaleY);
        var sourceWidth = Math.Max(1, (int)Math.Ceiling(sourceLayer.ActualWidth * renderDpiScaleX));
        var sourceHeight = Math.Max(1, (int)Math.Ceiling(sourceLayer.ActualHeight * renderDpiScaleY));

        var rendered = new RenderTargetBitmap(
            sourceWidth,
            sourceHeight,
            96 * renderDpiScaleX,
            96 * renderDpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(sourceLayer);
        rendered.Freeze();
        renderedSource = rendered;
        return true;
    }

    private bool TryUpdateBlur(FrameworkElement dialog, Border target, double renderScale)
    {
        dialog.UpdateLayout();
        return UpdateBlur(dialog, target, renderScale);
    }

    private Int32Rect GetTargetRect(
        FrameworkElement dialog,
        double renderDpiScaleX,
        double renderDpiScaleY,
        int sourceWidth,
        int sourceHeight)
    {
        var topLeft = dialog.TransformToVisual(sourceLayer).Transform(new Point(0, 0));
        var cropX = (int)Math.Round(topLeft.X * renderDpiScaleX);
        var cropY = (int)Math.Round(topLeft.Y * renderDpiScaleY);
        var cropWidth = Math.Max(1, (int)Math.Round(dialog.ActualWidth * renderDpiScaleX));
        var cropHeight = Math.Max(1, (int)Math.Round(dialog.ActualHeight * renderDpiScaleY));

        return IntersectWithSource(new Int32Rect(cropX, cropY, cropWidth, cropHeight), sourceWidth, sourceHeight);
    }

    private static ImageBrush EnsureLocalDialogBrush(Border target)
    {
        if (target.ReadLocalValue(Border.BackgroundProperty) is ImageBrush localBrush && !localBrush.IsFrozen)
            return localBrush;

        var brush = new ImageBrush
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute
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

    private static Int32Rect IntersectWithSource(Int32Rect rect, int sourceWidth, int sourceHeight)
    {
        var x = Math.Max(0, rect.X);
        var y = Math.Max(0, rect.Y);
        var right = Math.Min(sourceWidth, rect.X + rect.Width);
        var bottom = Math.Min(sourceHeight, rect.Y + rect.Height);

        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
