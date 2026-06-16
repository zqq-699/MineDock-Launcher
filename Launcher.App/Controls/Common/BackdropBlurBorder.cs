using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public sealed class BackdropBlurBorder : Border
{
    private static readonly TimeSpan DefaultRealtimeRefreshInterval = TimeSpan.FromMilliseconds(33);

    public static readonly DependencyProperty SourceElementProperty =
        DependencyProperty.Register(
            nameof(SourceElement),
            typeof(UIElement),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(null, OnBackdropPropertyChanged));

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(
            nameof(BlurRadius),
            typeof(double),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(42d, OnBackdropPropertyChanged));

    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register(
            nameof(TintColor),
            typeof(Color),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(Color.FromArgb(0x8A, 0x25, 0x25, 0x25), OnBackdropPropertyChanged));

    public static readonly DependencyProperty RenderScaleProperty =
        DependencyProperty.Register(
            nameof(RenderScale),
            typeof(double),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(1d, OnBackdropPropertyChanged));

    public static readonly DependencyProperty IsRealtimeRefreshEnabledProperty =
        DependencyProperty.Register(
            nameof(IsRealtimeRefreshEnabled),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(false, OnRealtimeRefreshEnabledChanged));

    public static readonly DependencyProperty IsSourceScrollRefreshEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSourceScrollRefreshEnabled),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(false, OnSourceRefreshPropertyChanged));

    public static readonly DependencyProperty BlurRenderingBiasProperty =
        DependencyProperty.Register(
            nameof(BlurRenderingBias),
            typeof(RenderingBias),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(RenderingBias.Performance, OnBackdropPropertyChanged));

    public static readonly DependencyProperty UseSourceElementAsRenderRootProperty =
        DependencyProperty.Register(
            nameof(UseSourceElementAsRenderRoot),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(false, OnBackdropPropertyChanged));

    public static readonly DependencyProperty SampleOffsetXProperty =
        DependencyProperty.Register(
            nameof(SampleOffsetX),
            typeof(double),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(0d, OnBackdropPropertyChanged));

    public static readonly DependencyProperty SampleOffsetYProperty =
        DependencyProperty.Register(
            nameof(SampleOffsetY),
            typeof(double),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(0d, OnBackdropPropertyChanged));

    public static readonly DependencyProperty UseSourceElementAsSampleOriginProperty =
        DependencyProperty.Register(
            nameof(UseSourceElementAsSampleOrigin),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(false, OnBackdropPropertyChanged));

    private bool isRefreshQueued;
    private bool isRenderRefreshQueued;
    private int queuedAttempts;
    private EventHandler? realtimeRefreshHandler;
    private ScrollViewer? observedSourceScrollViewer;
    private DateTime lastRealtimeRefreshUtc;

    public UIElement? SourceElement
    {
        get => (UIElement?)GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public double RenderScale
    {
        get => (double)GetValue(RenderScaleProperty);
        set => SetValue(RenderScaleProperty, value);
    }

    public bool IsRealtimeRefreshEnabled
    {
        get => (bool)GetValue(IsRealtimeRefreshEnabledProperty);
        set => SetValue(IsRealtimeRefreshEnabledProperty, value);
    }

    public bool IsSourceScrollRefreshEnabled
    {
        get => (bool)GetValue(IsSourceScrollRefreshEnabledProperty);
        set => SetValue(IsSourceScrollRefreshEnabledProperty, value);
    }

    public RenderingBias BlurRenderingBias
    {
        get => (RenderingBias)GetValue(BlurRenderingBiasProperty);
        set => SetValue(BlurRenderingBiasProperty, value);
    }

    public bool UseSourceElementAsRenderRoot
    {
        get => (bool)GetValue(UseSourceElementAsRenderRootProperty);
        set => SetValue(UseSourceElementAsRenderRootProperty, value);
    }

    public double SampleOffsetX
    {
        get => (double)GetValue(SampleOffsetXProperty);
        set => SetValue(SampleOffsetXProperty, value);
    }

    public double SampleOffsetY
    {
        get => (double)GetValue(SampleOffsetYProperty);
        set => SetValue(SampleOffsetYProperty, value);
    }

    public bool UseSourceElementAsSampleOrigin
    {
        get => (bool)GetValue(UseSourceElementAsSampleOriginProperty);
        set => SetValue(UseSourceElementAsSampleOriginProperty, value);
    }

    public BackdropBlurBorder()
    {
        Loaded += (_, _) =>
        {
            UpdateSourceScrollSubscription();
            QueueRefresh();
            if (IsRealtimeRefreshEnabled)
                StartRealtimeRefresh();
        };
        SizeChanged += (_, _) => QueueRefresh();
        IsVisibleChanged += (_, _) =>
        {
            UpdateSourceScrollSubscription();
            QueueRefresh();
        };
        Unloaded += (_, _) =>
        {
            StopRealtimeRefresh();
            ClearSourceScrollSubscription();
        };
    }

    private static void OnBackdropPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropBlurBorder border)
        {
            if (e.Property == SourceElementProperty)
                border.UpdateSourceScrollSubscription();

            border.QueueRefresh();
        }
    }

    private static void OnRealtimeRefreshEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BackdropBlurBorder border)
            return;

        if (e.NewValue is true && border.IsLoaded)
            border.StartRealtimeRefresh();
        else
            border.StopRealtimeRefresh();
    }

    private static void OnSourceRefreshPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropBlurBorder border)
            border.UpdateSourceScrollSubscription();
    }

    public void RequestRefresh()
    {
        QueueRefresh();
    }

    public void StartRealtimeRefresh(TimeSpan? interval = null)
    {
        StopRealtimeRefresh();
        var refreshInterval = interval.GetValueOrDefault(DefaultRealtimeRefreshInterval);
        lastRealtimeRefreshUtc = DateTime.MinValue;
        realtimeRefreshHandler = (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (now - lastRealtimeRefreshUtc < refreshInterval)
                return;

            lastRealtimeRefreshUtc = now;
            Refresh();
        };

        CompositionTarget.Rendering += realtimeRefreshHandler;
    }

    public void StopRealtimeRefresh()
    {
        if (realtimeRefreshHandler is null)
            return;

        CompositionTarget.Rendering -= realtimeRefreshHandler;
        realtimeRefreshHandler = null;
    }

    private void QueueRefresh(int attempts = 4)
    {
        queuedAttempts = Math.Max(queuedAttempts, attempts);
        if (isRefreshQueued)
            return;

        isRefreshQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                var remainingAttempts = queuedAttempts;
                queuedAttempts = 0;
                isRefreshQueued = false;

                if (!Refresh() && remainingAttempts > 0)
                    QueueRefresh(remainingAttempts - 1);
            },
            DispatcherPriority.ApplicationIdle);
    }

    private void QueueRenderRefresh()
    {
        if (isRenderRefreshQueued)
            return;

        isRenderRefreshQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isRenderRefreshQueued = false;
                Refresh();
            },
            DispatcherPriority.Render);
    }

    private void UpdateSourceScrollSubscription()
    {
        var scrollViewer = IsLoaded && IsVisible && IsSourceScrollRefreshEnabled
            ? SourceElement as ScrollViewer
            : null;

        if (ReferenceEquals(observedSourceScrollViewer, scrollViewer))
            return;

        ClearSourceScrollSubscription();
        observedSourceScrollViewer = scrollViewer;
        if (observedSourceScrollViewer is not null)
            observedSourceScrollViewer.ScrollChanged += SourceScrollViewer_ScrollChanged;
    }

    private void ClearSourceScrollSubscription()
    {
        if (observedSourceScrollViewer is null)
            return;

        observedSourceScrollViewer.ScrollChanged -= SourceScrollViewer_ScrollChanged;
        observedSourceScrollViewer = null;
    }

    private void SourceScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueRenderRefresh();
    }

    private bool Refresh()
    {
        if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0 || SourceElement is null)
            return false;

        var window = Window.GetWindow(SourceElement);
        var sourceRoot = UseSourceElementAsRenderRoot
            ? SourceElement as FrameworkElement
            : window?.Content as FrameworkElement;
        sourceRoot ??= window?.Content as FrameworkElement;
        if (sourceRoot is null
            || sourceRoot.ActualWidth <= 0
            || sourceRoot.ActualHeight <= 0)
        {
            ApplyFallbackBackground();
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(sourceRoot);
        var renderScale = Math.Clamp(RenderScale, 0.35d, 1d);
        var renderDpiScaleX = dpi.DpiScaleX * renderScale;
        var renderDpiScaleY = dpi.DpiScaleY * renderScale;
        var renderDpi = new DpiScale(renderDpiScaleX, renderDpiScaleY);
        var sourceWidth = Math.Max(1, (int)Math.Ceiling(sourceRoot.ActualWidth * renderDpiScaleX));
        var sourceHeight = Math.Max(1, (int)Math.Ceiling(sourceRoot.ActualHeight * renderDpiScaleY));

        var rendered = new RenderTargetBitmap(
            sourceWidth,
            sourceHeight,
            96 * renderDpiScaleX,
            96 * renderDpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(sourceRoot);

        Point topLeft;
        if (UseSourceElementAsSampleOrigin
            && SourceElement is Visual sourceVisual)
        {
            try
            {
                topLeft = sourceVisual.TransformToVisual(sourceRoot).Transform(new Point(0, 0));
                topLeft.Offset(SampleOffsetX, SampleOffsetY);
            }
            catch (InvalidOperationException)
            {
                var sampleOrigin = SourceElement.PointToScreen(new Point(0, 0));
                sampleOrigin.Offset(SampleOffsetX, SampleOffsetY);
                topLeft = sourceRoot.PointFromScreen(sampleOrigin);
            }
        }
        else
        {
            var sampleOrigin = PointToScreen(new Point(0, 0));
            sampleOrigin.Offset(SampleOffsetX, SampleOffsetY);
            topLeft = sourceRoot.PointFromScreen(sampleOrigin);
        }
        var cropX = (int)Math.Round(topLeft.X * renderDpiScaleX);
        var cropY = (int)Math.Round(topLeft.Y * renderDpiScaleY);
        var cropWidth = Math.Max(1, (int)Math.Round(ActualWidth * renderDpiScaleX));
        var cropHeight = Math.Max(1, (int)Math.Round(ActualHeight * renderDpiScaleY));

        var targetRect = IntersectWithSource(new Int32Rect(cropX, cropY, cropWidth, cropHeight), sourceWidth, sourceHeight);
        if (targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            ApplyFallbackBackground();
            return false;
        }

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
        {
            ApplyFallbackBackground();
            return false;
        }

        var expandedCrop = new CroppedBitmap(rendered, expandedRect);
        expandedCrop.Freeze();

        var blurredExpanded = CreateBlurredBitmap(expandedCrop, renderDpi, BlurRadius, BlurRenderingBias);
        var blurredTarget = new CroppedBitmap(
            blurredExpanded,
            new Int32Rect(
                targetRect.X - expandedRect.X,
                targetRect.Y - expandedRect.Y,
                targetRect.Width,
                targetRect.Height));
        blurredTarget.Freeze();

        Background = new ImageBrush(CreateBackgroundBitmap(blurredTarget, renderDpi, TintColor))
        {
            Stretch = Stretch.Fill
        };
        return true;
    }

    private void ApplyFallbackBackground()
    {
        Background = new SolidColorBrush(TintColor);
    }

    private static BitmapSource CreateBlurredBitmap(BitmapSource source, DpiScale dpi, double blurRadius, RenderingBias renderingBias)
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
                Radius = blurRadius,
                RenderingBias = renderingBias
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

    private static BitmapSource CreateBackgroundBitmap(BitmapSource blurredSource, DpiScale dpi, Color tintColor)
    {
        var width = blurredSource.PixelWidth / dpi.DpiScaleX;
        var height = blurredSource.PixelHeight / dpi.DpiScaleY;
        var rect = new Rect(0, 0, width, height);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), null, rect);
            context.DrawImage(blurredSource, rect);
            context.DrawRectangle(new SolidColorBrush(tintColor), null, rect);
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
        var x = Math.Clamp(rect.X, 0, sourceWidth);
        var y = Math.Clamp(rect.Y, 0, sourceHeight);
        var right = Math.Clamp(rect.X + rect.Width, 0, sourceWidth);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, sourceHeight);

        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
