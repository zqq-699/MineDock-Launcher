using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using Launcher.App.Services;
using Launcher.App.Utilities;

namespace Launcher.App.Controls;

[ContentProperty(nameof(DialogContent))]
public partial class DialogHost : UserControl
{
    private static readonly Duration FadeInDuration = TimeSpan.FromMilliseconds(140);
    private static readonly Duration FadeOutDuration = TimeSpan.FromMilliseconds(180);

    public static readonly DependencyProperty DialogWidthProperty =
        DependencyProperty.Register(nameof(DialogWidth), typeof(double), typeof(DialogHost), new PropertyMetadata(420d));

    public static readonly DependencyProperty DialogContentProperty =
        DependencyProperty.Register(nameof(DialogContent), typeof(object), typeof(DialogHost), new PropertyMetadata(null));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(DialogHost), new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly DependencyProperty UseIntegratedOverlayProperty =
        DependencyProperty.Register(
            nameof(UseIntegratedOverlay),
            typeof(bool),
            typeof(DialogHost),
            new PropertyMetadata(false, OnIntegratedOverlayPropertyChanged));

    public static readonly DependencyProperty OverlaySourceElementProperty =
        DependencyProperty.Register(
            nameof(OverlaySourceElement),
            typeof(FrameworkElement),
            typeof(DialogHost),
            new PropertyMetadata(null, OnIntegratedOverlayPropertyChanged));

    private DialogOverlayService? integratedOverlayService;
    private Window? ownerWindow;
    private bool suppressIsOpenChanged;

    public DialogHost()
    {
        InitializeComponent();
        Loaded += DialogHost_Loaded;
        Unloaded += DialogHost_Unloaded;
        SizeChanged += DialogHost_SizeChanged;
        SurfaceBorder.SizeChanged += SurfaceBorder_SizeChanged;
    }

    public double DialogWidth
    {
        get => (double)GetValue(DialogWidthProperty);
        set => SetValue(DialogWidthProperty, value);
    }

    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool UseIntegratedOverlay
    {
        get => (bool)GetValue(UseIntegratedOverlayProperty);
        set => SetValue(UseIntegratedOverlayProperty, value);
    }

    public FrameworkElement? OverlaySourceElement
    {
        get => (FrameworkElement?)GetValue(OverlaySourceElementProperty);
        set => SetValue(OverlaySourceElementProperty, value);
    }

    public bool IsSizeAnimating => integratedOverlayService?.IsSizeAnimating ?? false;

    public Grid OverlayRoot => RootOverlay;

    public Border SurfaceBorder => Surface;

    public Border BlurLayerBorder => BlurLayer;

    public void Show()
    {
        SetIsOpenValue(true);
        SetIsOpenCore(true);
    }

    public void Hide(Action? completed = null)
    {
        SetIsOpenValue(false);
        SetIsOpenCore(false, completed);
    }

    public void Prewarm()
    {
        if (EnsureIntegratedOverlayService())
            integratedOverlayService!.Prewarm(this);
    }

    public void QueueRefresh(int attempts = 5)
    {
        if (!IsOpen || !EnsureIntegratedOverlayService())
            return;

        integratedOverlayService!.QueueRefresh(this, attempts);
    }

    public void AnimateSizeChange(double previousHeight)
    {
        if (!EnsureIntegratedOverlayService())
            return;

        integratedOverlayService!.AnimateSizeChange(this, previousHeight);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DialogHost host || host.suppressIsOpenChanged)
            return;

        host.SetIsOpenCore((bool)e.NewValue);
    }

    private static void OnIntegratedOverlayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DialogHost host || !host.IsLoaded)
            return;

        host.ResetIntegratedOverlayService();
        if (host.UseIntegratedOverlay)
        {
            host.EnsureIntegratedOverlayService();
            if (host.IsOpen)
                host.SetIsOpenCore(true);
        }
    }

    private void DialogHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (UseIntegratedOverlay)
        {
            EnsureIntegratedOverlayService();
            Prewarm();
            if (IsOpen)
                SetIsOpenCore(true);
        }
    }

    private void DialogHost_Unloaded(object sender, RoutedEventArgs e)
    {
        ResetIntegratedOverlayService();
    }

    private void DialogHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueIntegratedOverlayRefresh();
    }

    private void SurfaceBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueIntegratedOverlayRefresh();
    }

    private void OwnerWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        QueueIntegratedOverlayRefresh();
    }

    private void SetIsOpenValue(bool value)
    {
        suppressIsOpenChanged = true;
        SetCurrentValue(IsOpenProperty, value);
        suppressIsOpenChanged = false;
    }

    private void SetIsOpenCore(bool isOpen, Action? completed = null)
    {
        if (UseIntegratedOverlay && EnsureIntegratedOverlayService())
        {
            if (isOpen)
                integratedOverlayService!.Show(this);
            else
                integratedOverlayService!.Hide(this, completed);

            return;
        }

        OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);

        if (isOpen)
        {
            OverlayRoot.Visibility = Visibility.Visible;
            OverlayRoot.Opacity = 0;
            OverlayRoot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, FadeInDuration)
                {
                    FillBehavior = FillBehavior.Stop
                });
            OverlayRoot.Opacity = 1;
            return;
        }

        var currentOpacity = OverlayRoot.Opacity;
        if (currentOpacity <= 0 || OverlayRoot.Visibility != Visibility.Visible)
        {
            OverlayRoot.Visibility = Visibility.Collapsed;
            OverlayRoot.Opacity = 0;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(currentOpacity, 0, FadeOutDuration)
        {
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            OverlayRoot.Visibility = Visibility.Collapsed;
            OverlayRoot.Opacity = 0;
            completed?.Invoke();
        };

        OverlayRoot.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private bool EnsureIntegratedOverlayService()
    {
        if (!UseIntegratedOverlay)
            return false;

        if (integratedOverlayService is not null)
            return true;

        ownerWindow = Window.GetWindow(this);
        if (ownerWindow is null)
            return false;

        var sourceElement = OverlaySourceElement
            ?? VisualTreeSearch.FindDescendant<Grid>(
                ownerWindow,
                grid => string.Equals(grid.Name, "WindowContentLayer", StringComparison.Ordinal))
            ?? ownerWindow.Content as FrameworkElement;
        if (sourceElement is null)
            return false;

        integratedOverlayService = new DialogOverlayService(ownerWindow, sourceElement);
        ownerWindow.SizeChanged += OwnerWindow_SizeChanged;
        return true;
    }

    private void ResetIntegratedOverlayService()
    {
        if (ownerWindow is not null)
        {
            ownerWindow.SizeChanged -= OwnerWindow_SizeChanged;
            ownerWindow = null;
        }

        integratedOverlayService = null;
    }

    private void QueueIntegratedOverlayRefresh()
    {
        if (!UseIntegratedOverlay || !IsOpen || integratedOverlayService is null)
            return;

        integratedOverlayService.QueueRefresh(this);
    }
}
