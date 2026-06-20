using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public sealed class BackdropBlurBorder : Border
{
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

    public static readonly DependencyProperty TintBrushProperty =
        DependencyProperty.Register(
            nameof(TintBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(null, OnBackdropPropertyChanged));

    public static readonly DependencyProperty BaseBrushProperty =
        DependencyProperty.Register(
            nameof(BaseBrush),
            typeof(Brush),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(null, OnBackdropPropertyChanged));

    public static readonly DependencyProperty RenderScaleProperty =
        DependencyProperty.Register(
            nameof(RenderScale),
            typeof(double),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(1d));

    public static readonly DependencyProperty IsRealtimeRefreshEnabledProperty =
        DependencyProperty.Register(
            nameof(IsRealtimeRefreshEnabled),
            typeof(bool),
            typeof(BackdropBlurBorder),
            new PropertyMetadata(false));

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

    private readonly Grid rootLayer;
    private readonly Border baseLayer;
    private readonly Border blurLayer;
    private readonly Border tintLayer;
    private readonly ContentPresenter contentPresenter;
    private ScrollViewer? observedSourceScrollViewer;
    private bool isUpdatingChild;
    private bool isRefreshQueued;

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

    public Brush? TintBrush
    {
        get => (Brush?)GetValue(TintBrushProperty);
        set => SetValue(TintBrushProperty, value);
    }

    public Brush? BaseBrush
    {
        get => (Brush?)GetValue(BaseBrushProperty);
        set => SetValue(BaseBrushProperty, value);
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
        SetResourceReference(BaseBrushProperty, "Brush.Backdrop.Base");
        SetResourceReference(TintBrushProperty, "Brush.Backdrop.Tint");

        baseLayer = CreateLayer();
        blurLayer = CreateLayer();
        tintLayer = CreateLayer();
        contentPresenter = new ContentPresenter
        {
            SnapsToDevicePixels = true
        };
        rootLayer = new Grid
        {
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };
        rootLayer.Children.Add(baseLayer);
        rootLayer.Children.Add(blurLayer);
        rootLayer.Children.Add(tintLayer);
        rootLayer.Children.Add(contentPresenter);

        SetInternalChild(rootLayer);

        Loaded += (_, _) =>
        {
            ApplyLayerCornerRadius();
            UpdateSourceScrollSubscription();
            QueueRefresh();
        };
        SizeChanged += (_, _) => QueueRefresh();
        IsVisibleChanged += (_, _) =>
        {
            UpdateSourceScrollSubscription();
            QueueRefresh();
        };
        Unloaded += (_, _) => ClearSourceScrollSubscription();
    }

    public void RequestRefresh()
    {
        QueueRefresh();
    }

    public void StartRealtimeRefresh(TimeSpan? interval = null)
    {
        QueueRefresh();
    }

    public void StopRealtimeRefresh()
    {
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property.Name == nameof(Child) && !isUpdatingChild)
        {
            MoveExternalChildIntoPresenter(e.NewValue as UIElement);
            return;
        }

        if (e.Property == CornerRadiusProperty)
        {
            ApplyLayerCornerRadius();
        }
    }

    private static Border CreateLayer()
    {
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
    }

    private void SetInternalChild(UIElement child)
    {
        if (ReferenceEquals(Child, child))
            return;

        isUpdatingChild = true;
        Child = child;
        isUpdatingChild = false;
    }

    private void MoveExternalChildIntoPresenter(UIElement? child)
    {
        if (child is null)
        {
            contentPresenter.Content = null;
            SetInternalChild(rootLayer);
            return;
        }

        if (ReferenceEquals(child, rootLayer))
            return;

        SetInternalChild(rootLayer);
        contentPresenter.Content = child;
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

    private static void OnSourceRefreshPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropBlurBorder border)
            border.UpdateSourceScrollSubscription();
    }

    private void QueueRefresh()
    {
        if (isRefreshQueued)
            return;

        isRefreshQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isRefreshQueued = false;
                Refresh();
            },
            DispatcherPriority.Render);
    }

    private void Refresh()
    {
        if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0 || SourceElement is null)
        {
            ApplyFallbackBackground();
            return;
        }

        var sourceRoot = ResolveSourceRoot();
        if (sourceRoot is null || sourceRoot.ActualWidth <= 0 || sourceRoot.ActualHeight <= 0)
        {
            ApplyFallbackBackground();
            return;
        }

        var sourceBrush = new VisualBrush(sourceRoot)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = ResolveViewbox(sourceRoot)
        };

        ApplyLayerCornerRadius();
        baseLayer.Background = BaseBrush;
        blurLayer.Background = sourceBrush;
        blurLayer.Effect = new BlurEffect
        {
            Radius = BlurRadius,
            RenderingBias = BlurRenderingBias
        };
        tintLayer.Background = TintBrush;
    }

    private FrameworkElement? ResolveSourceRoot()
    {
        if (UseSourceElementAsRenderRoot)
            return SourceElement as FrameworkElement;

        var window = Window.GetWindow(SourceElement);
        return window?.Content as FrameworkElement;
    }

    private Rect ResolveViewbox(FrameworkElement sourceRoot)
    {
        var sampleOrigin = UseSourceElementAsSampleOrigin && SourceElement is not null
            ? SourceElement.PointToScreen(new Point(SampleOffsetX, SampleOffsetY))
            : PointToScreen(new Point(SampleOffsetX, SampleOffsetY));
        var topLeft = sourceRoot.PointFromScreen(sampleOrigin);
        return new Rect(topLeft.X, topLeft.Y, ActualWidth, ActualHeight);
    }

    private void ApplyFallbackBackground()
    {
        ApplyLayerCornerRadius();
        baseLayer.Background = BaseBrush;
        blurLayer.Background = null;
        blurLayer.Effect = null;
        tintLayer.Background = TintBrush;
    }

    private void ApplyLayerCornerRadius()
    {
        baseLayer.CornerRadius = CornerRadius;
        blurLayer.CornerRadius = CornerRadius;
        tintLayer.CornerRadius = CornerRadius;
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
        QueueRefresh();
    }
}
