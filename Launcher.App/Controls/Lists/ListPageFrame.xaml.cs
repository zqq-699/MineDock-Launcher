using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.App.Behaviors;
using Launcher.App.Effects;
using Serilog;

namespace Launcher.App.Controls;

public partial class ListPageFrame : UserControl
{
    private const double DefaultProgressiveBlurMaximumRadius = 24d;
    private const double DefaultProgressiveBlurRenderScale = 0.4d;
    private const double DefaultProgressiveBlurMinimumOpacity = 0d;
    private const double DefaultProgressiveBlurIntermediateOpacity = 0.4d;
    private const double ProgressiveBlurSamplingGuardLength = 24d;
    private const double ProgressiveBlurTextureOverscanLength = 4d;
    private const double MinimumProgressiveBlurRenderScale = 0.1d;

    private static readonly DependencyPropertyDescriptor? TopFadeLengthDescriptor =
        DependencyPropertyDescriptor.FromProperty(
            VerticalEdgeOpacityMask.TopFadeLengthProperty,
            typeof(Grid));

    private static int progressiveBlurFailureLogged;

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ListPageFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleIconSourceProperty =
        DependencyProperty.Register(nameof(TitleIconSource), typeof(ImageSource), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsHeaderBackButtonVisibleProperty =
        DependencyProperty.Register(nameof(IsHeaderBackButtonVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty HeaderBackCommandProperty =
        DependencyProperty.Register(nameof(HeaderBackCommand), typeof(ICommand), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(ListPageFrame),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsSearchVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty SearchTrailingContentProperty =
        DependencyProperty.Register(nameof(SearchTrailingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchToolbarContentProperty =
        DependencyProperty.Register(nameof(SearchToolbarContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchToolbarVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchToolbarVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchToolbarContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchToolbarContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty SearchFilterContentProperty =
        DependencyProperty.Register(nameof(SearchFilterContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSearchFilterVisibleProperty =
        DependencyProperty.Register(nameof(IsSearchFilterVisible), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchFilterContentTemplateProperty =
        DependencyProperty.Register(nameof(SearchFilterContentTemplate), typeof(DataTemplate), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsListVisibleProperty =
        DependencyProperty.Register(
            nameof(IsListVisible),
            typeof(bool),
            typeof(ListPageFrame),
            new PropertyMetadata(true, OnProgressiveBlurStateChanged));

    public static readonly DependencyProperty IsProgressiveBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsProgressiveBlurEnabled),
            typeof(bool),
            typeof(ListPageFrame),
            new PropertyMetadata(false, OnProgressiveBlurStateChanged));

    public static readonly DependencyProperty UseFrameScrollViewerProperty =
        DependencyProperty.Register(nameof(UseFrameScrollViewer), typeof(bool), typeof(ListPageFrame), new PropertyMetadata(true));

    public static readonly DependencyProperty OverlayContentProperty =
        DependencyProperty.Register(nameof(OverlayContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty ListContentProperty =
        DependencyProperty.Register(nameof(ListContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty FloatingContentProperty =
        DependencyProperty.Register(nameof(FloatingContent), typeof(object), typeof(ListPageFrame), new PropertyMetadata(null));

    public ListPageFrame()
    {
        InitializeComponent();
        PART_BlurBandBrush.Visual = PART_ListVisualSource;
        Loaded += ListPageFrame_Loaded;
        Unloaded += ListPageFrame_Unloaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ImageSource? TitleIconSource
    {
        get => (ImageSource?)GetValue(TitleIconSourceProperty);
        set => SetValue(TitleIconSourceProperty, value);
    }

    public bool IsHeaderBackButtonVisible
    {
        get => (bool)GetValue(IsHeaderBackButtonVisibleProperty);
        set => SetValue(IsHeaderBackButtonVisibleProperty, value);
    }

    public ICommand? HeaderBackCommand
    {
        get => (ICommand?)GetValue(HeaderBackCommandProperty);
        set => SetValue(HeaderBackCommandProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool IsSearchVisible
    {
        get => (bool)GetValue(IsSearchVisibleProperty);
        set => SetValue(IsSearchVisibleProperty, value);
    }

    public object? SearchTrailingContent
    {
        get => GetValue(SearchTrailingContentProperty);
        set => SetValue(SearchTrailingContentProperty, value);
    }

    public object? SearchToolbarContent
    {
        get => GetValue(SearchToolbarContentProperty);
        set => SetValue(SearchToolbarContentProperty, value);
    }

    public bool IsSearchToolbarVisible
    {
        get => (bool)GetValue(IsSearchToolbarVisibleProperty);
        set => SetValue(IsSearchToolbarVisibleProperty, value);
    }

    public DataTemplate? SearchToolbarContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchToolbarContentTemplateProperty);
        set => SetValue(SearchToolbarContentTemplateProperty, value);
    }

    public object? SearchFilterContent
    {
        get => GetValue(SearchFilterContentProperty);
        set => SetValue(SearchFilterContentProperty, value);
    }

    public bool IsSearchFilterVisible
    {
        get => (bool)GetValue(IsSearchFilterVisibleProperty);
        set => SetValue(IsSearchFilterVisibleProperty, value);
    }

    public DataTemplate? SearchFilterContentTemplate
    {
        get => (DataTemplate?)GetValue(SearchFilterContentTemplateProperty);
        set => SetValue(SearchFilterContentTemplateProperty, value);
    }

    public bool IsListVisible
    {
        get => (bool)GetValue(IsListVisibleProperty);
        set => SetValue(IsListVisibleProperty, value);
    }

    public bool IsProgressiveBlurEnabled
    {
        get => (bool)GetValue(IsProgressiveBlurEnabledProperty);
        set => SetValue(IsProgressiveBlurEnabledProperty, value);
    }

    public bool UseFrameScrollViewer
    {
        get => (bool)GetValue(UseFrameScrollViewerProperty);
        set => SetValue(UseFrameScrollViewerProperty, value);
    }

    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public object? ListContent
    {
        get => GetValue(ListContentProperty);
        set => SetValue(ListContentProperty, value);
    }

    public object? FloatingContent
    {
        get => GetValue(FloatingContentProperty);
        set => SetValue(FloatingContentProperty, value);
    }

    public ScrollViewer ScrollViewer => PART_ScrollViewer;

    internal FrameworkElement ListLayerElement => PART_ListLayer;

    internal FrameworkElement ListVisualSourceElement => PART_ListVisualSource;

    internal FrameworkElement DirectListHostElement => PART_DirectListHost;

    internal FrameworkElement BlurBandViewportElement => PART_BlurBandViewport;

    internal FrameworkElement BlurBandUpscaleHostElement => PART_BlurBandUpscaleHost;

    internal ScaleTransform BlurBandUpscaleTransform => PART_BlurBandUpscaleTransform;

    internal FrameworkElement BlurBandHorizontalHostElement => PART_BlurBandHorizontalHost;

    internal FrameworkElement BlurBandVerticalHostElement => PART_BlurBandVerticalHost;

    internal VisualBrush BlurBandBrush => PART_BlurBandBrush;

    internal FrameworkElement HeaderOverlayElement => PART_HeaderOverlay;

    internal FrameworkElement HeaderTitleRowElement => PART_HeaderTitleRow;

    private ProgressiveGaussianBlurEffect? horizontalProgressiveBlurEffect;
    private ProgressiveGaussianBlurEffect? verticalProgressiveBlurEffect;
    private readonly RectangleGeometry directListClipGeometry = new();
    private bool progressiveBlurSubscriptionsAttached;
    private Window? progressiveBlurDpiWindow;

    internal readonly record struct ProgressiveBlurRenderLayout(
        double LowResolutionWidth,
        double LowResolutionHeight,
        double UpscaleX,
        double UpscaleY,
        double ScaledBlurLength,
        double HorizontalMaximumRadius,
        double VerticalMaximumRadius,
        double TextureHeight,
        double PresentationHeight,
        double DirectListStart);

    private static void OnProgressiveBlurStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ListPageFrame frame)
            frame.UpdateProgressiveBlur();
    }

    private void ListPageFrame_Loaded(object sender, RoutedEventArgs e)
    {
        AttachProgressiveBlurSubscriptions();
        UpdateProgressiveBlur();
    }

    private void ListPageFrame_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachProgressiveBlurSubscriptions();
        DeactivateProgressiveBlur();
    }

    private void AttachProgressiveBlurSubscriptions()
    {
        if (progressiveBlurSubscriptionsAttached)
            return;

        PART_ListLayer.SizeChanged += ListLayer_SizeChanged;
        TopFadeLengthDescriptor?.AddValueChanged(PART_ListLayer, ListLayer_TopFadeLengthChanged);
        AttachProgressiveBlurDpiSubscription();
        progressiveBlurSubscriptionsAttached = true;
    }

    private void DetachProgressiveBlurSubscriptions()
    {
        if (!progressiveBlurSubscriptionsAttached)
            return;

        PART_ListLayer.SizeChanged -= ListLayer_SizeChanged;
        TopFadeLengthDescriptor?.RemoveValueChanged(PART_ListLayer, ListLayer_TopFadeLengthChanged);
        DetachProgressiveBlurDpiSubscription();
        progressiveBlurSubscriptionsAttached = false;
    }

    private void AttachProgressiveBlurDpiSubscription()
    {
        var ownerWindow = Window.GetWindow(this);
        if (ReferenceEquals(progressiveBlurDpiWindow, ownerWindow))
            return;

        DetachProgressiveBlurDpiSubscription();
        progressiveBlurDpiWindow = ownerWindow;
        if (progressiveBlurDpiWindow is not null)
            progressiveBlurDpiWindow.DpiChanged += ProgressiveBlurWindow_DpiChanged;
    }

    private void DetachProgressiveBlurDpiSubscription()
    {
        if (progressiveBlurDpiWindow is null)
            return;

        progressiveBlurDpiWindow.DpiChanged -= ProgressiveBlurWindow_DpiChanged;
        progressiveBlurDpiWindow = null;
    }

    private void ProgressiveBlurWindow_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateProgressiveBlur();
    }

    private void ListLayer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressiveBlur();
    }

    private void ListLayer_TopFadeLengthChanged(object? sender, EventArgs e)
    {
        UpdateProgressiveBlur();
    }

    private void UpdateProgressiveBlur()
    {
        if (!IsLoaded || !IsListVisible || !IsProgressiveBlurEnabled)
        {
            DeactivateProgressiveBlur();
            return;
        }

        var width = PART_ListLayer.ActualWidth;
        var height = PART_ListLayer.ActualHeight;
        var blurLength = ResolveEffectiveTopBlurLength(height);
        var visibleBlurBandHeight = Math.Min(height, blurLength + ProgressiveBlurSamplingGuardLength);
        if (width <= 0d || height <= 0d || blurLength <= 0d || visibleBlurBandHeight <= 0d)
        {
            DeactivateProgressiveBlur();
            return;
        }

        try
        {
            if (!EnsureProgressiveBlurEffects())
            {
                DeactivateProgressiveBlur();
                return;
            }

            var maximumRadius = ResolveDoubleResource(
                "ListPage.ProgressiveBlur.MaxRadius",
                DefaultProgressiveBlurMaximumRadius,
                minimum: 0d,
                maximum: double.MaxValue);
            var renderScale = ResolveDoubleResource(
                "ListPage.ProgressiveBlur.RenderScale",
                DefaultProgressiveBlurRenderScale,
                minimum: MinimumProgressiveBlurRenderScale,
                maximum: 1d);
            var renderLayout = CalculateProgressiveBlurRenderLayout(
                width,
                height,
                blurLength,
                visibleBlurBandHeight,
                maximumRadius,
                renderScale,
                VisualTreeHelper.GetDpi(PART_ListLayer));

            UpdateProgressiveBlurBandLayout(width, height, renderLayout);
            ApplyProgressiveBlurParameters(
                horizontalProgressiveBlurEffect!,
                renderLayout.LowResolutionWidth,
                renderLayout.LowResolutionHeight,
                renderLayout.ScaledBlurLength,
                renderLayout.HorizontalMaximumRadius);
            ApplyProgressiveBlurParameters(
                verticalProgressiveBlurEffect!,
                renderLayout.LowResolutionWidth,
                renderLayout.LowResolutionHeight,
                renderLayout.ScaledBlurLength,
                renderLayout.VerticalMaximumRadius);

            PART_BlurBandHorizontalHost.Effect = horizontalProgressiveBlurEffect;
            PART_BlurBandVerticalHost.Effect = verticalProgressiveBlurEffect;
            PART_BlurBandViewport.Visibility = Visibility.Visible;
            VerticalEdgeOpacityMask.SetTopMinimumOpacity(
                PART_ListLayer,
                ResolveDoubleResource(
                    "ListPage.ProgressiveBlur.ActiveMinimumOpacity",
                    DefaultProgressiveBlurMinimumOpacity,
                    minimum: 0d,
                    maximum: 1d));
            VerticalEdgeOpacityMask.SetTopIntermediateOpacity(
                PART_ListLayer,
                ResolveDoubleResource(
                    "ListPage.ProgressiveBlur.ActiveIntermediateOpacity",
                    DefaultProgressiveBlurIntermediateOpacity,
                    minimum: 0d,
                    maximum: 1d));
        }
        catch (Exception exception)
        {
            DeactivateProgressiveBlur();
            LogProgressiveBlurFailureOnce(exception);
        }
    }

    private bool EnsureProgressiveBlurEffects()
    {
        if (horizontalProgressiveBlurEffect is not null && verticalProgressiveBlurEffect is not null)
            return true;

        if (!TryCreateProgressiveBlurEffect(1d, 0d, out var horizontalEffect) ||
            !TryCreateProgressiveBlurEffect(0d, 1d, out var verticalEffect))
            return false;

        horizontalProgressiveBlurEffect = horizontalEffect;
        verticalProgressiveBlurEffect = verticalEffect;
        return true;
    }

    private static bool TryCreateProgressiveBlurEffect(
        double directionX,
        double directionY,
        out ProgressiveGaussianBlurEffect? effect)
    {
        if (ProgressiveGaussianBlurEffect.TryCreate(directionX, directionY, out effect, out var exception) &&
            effect is not null)
            return true;

        LogProgressiveBlurFailureOnce(
            exception ?? new InvalidOperationException("Progressive blur shader did not create an effect instance."));
        return false;
    }

    private static void ApplyProgressiveBlurParameters(
        ProgressiveGaussianBlurEffect effect,
        double width,
        double height,
        double blurLength,
        double maximumRadius)
    {
        effect.InputWidth = Math.Max(1d, width);
        effect.InputHeight = Math.Max(1d, height);
        effect.BlurLength = Math.Clamp(blurLength, 0d, height);
        effect.MaximumRadius = Math.Max(0d, maximumRadius);
    }

    internal static ProgressiveBlurRenderLayout CalculateProgressiveBlurRenderLayout(
        double width,
        double height,
        double blurLength,
        double visibleBlurBandHeight,
        double maximumRadius,
        double renderScale,
        DpiScale dpiScale)
    {
        var seamY = Math.Clamp(
            AlignToDevicePixel(visibleBlurBandHeight, dpiScale.DpiScaleY),
            0d,
            height);
        var textureHeight = Math.Min(
            height,
            seamY + ProgressiveBlurTextureOverscanLength);
        var lowResolutionWidth = CalculateLowResolutionDimension(width, dpiScale.DpiScaleX, renderScale);
        var lowResolutionHeight = CalculateLowResolutionDimension(textureHeight, dpiScale.DpiScaleY, renderScale);
        var horizontalRatio = lowResolutionWidth / width;
        var verticalRatio = lowResolutionHeight / textureHeight;

        return new ProgressiveBlurRenderLayout(
            lowResolutionWidth,
            lowResolutionHeight,
            width / lowResolutionWidth,
            textureHeight / lowResolutionHeight,
            Math.Clamp(blurLength * verticalRatio, 0d, lowResolutionHeight),
            Math.Max(0d, maximumRadius * horizontalRatio),
            Math.Max(0d, maximumRadius * verticalRatio),
            textureHeight,
            seamY,
            seamY);
    }

    private static double AlignToDevicePixel(double value, double dpiScale)
    {
        return Math.Round(value * dpiScale, MidpointRounding.AwayFromZero) / dpiScale;
    }

    private static double CalculateLowResolutionDimension(double fullSize, double dpiScale, double renderScale)
    {
        var lowResolutionPixels = Math.Max(
            1d,
            Math.Round(fullSize * dpiScale * renderScale, MidpointRounding.AwayFromZero));
        return Math.Min(fullSize, lowResolutionPixels / dpiScale);
    }

    private void UpdateProgressiveBlurBandLayout(
        double width,
        double height,
        ProgressiveBlurRenderLayout renderLayout)
    {
        PART_BlurBandViewport.Height = renderLayout.PresentationHeight;
        PART_BlurBandUpscaleHost.Width = renderLayout.LowResolutionWidth;
        PART_BlurBandUpscaleHost.Height = renderLayout.LowResolutionHeight;
        PART_BlurBandHorizontalHost.Width = renderLayout.LowResolutionWidth;
        PART_BlurBandHorizontalHost.Height = renderLayout.LowResolutionHeight;
        PART_BlurBandVerticalHost.Width = renderLayout.LowResolutionWidth;
        PART_BlurBandVerticalHost.Height = renderLayout.LowResolutionHeight;
        PART_BlurBandUpscaleTransform.ScaleX = renderLayout.UpscaleX;
        PART_BlurBandUpscaleTransform.ScaleY = renderLayout.UpscaleY;
        PART_BlurBandBrush.Viewbox = new Rect(0d, 0d, width, renderLayout.TextureHeight);

        directListClipGeometry.Rect = new Rect(
            0d,
            renderLayout.DirectListStart,
            width,
            Math.Max(0d, height - renderLayout.DirectListStart));
        PART_DirectListHost.Clip = directListClipGeometry;
    }

    private double ResolveEffectiveTopBlurLength(double height)
    {
        if (height <= 0d)
            return 0d;

        var topLength = Math.Clamp(VerticalEdgeOpacityMask.GetTopFadeLength(PART_ListLayer), 0d, height);
        var bottomLength = Math.Clamp(VerticalEdgeOpacityMask.GetBottomFadeLength(PART_ListLayer), 0d, height);
        var totalLength = topLength + bottomLength;
        if (totalLength > height && totalLength > 0d)
            topLength *= height / totalLength;

        return topLength;
    }

    private double ResolveDoubleResource(string key, double fallback, double minimum, double maximum)
    {
        var value = TryFindResource(key) is double resourceValue ? resourceValue : fallback;
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    private void DeactivateProgressiveBlur()
    {
        PART_ListLayer.Effect = null;
        PART_ListVisualSource.Effect = null;
        PART_BlurBandVerticalHost.Effect = null;
        PART_BlurBandHorizontalHost.Effect = null;
        PART_BlurBandViewport.Visibility = Visibility.Collapsed;
        PART_DirectListHost.Clip = null;
        PART_ListLayer.ClearValue(VerticalEdgeOpacityMask.TopMinimumOpacityProperty);
        PART_ListLayer.ClearValue(VerticalEdgeOpacityMask.TopIntermediateOpacityProperty);
        horizontalProgressiveBlurEffect = null;
        verticalProgressiveBlurEffect = null;
    }

    private static void LogProgressiveBlurFailureOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref progressiveBlurFailureLogged, 1) != 0)
            return;

        Log.Warning(
            exception,
            "Progressive blur effect activation failed; opacity fade fallback will be used.");
    }
}
