using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.ViewModels.Home;

namespace Launcher.App.Views.Home;

public partial class HomeLaunchGameListView : UserControl
{
    public static readonly DependencyProperty SuppressSelectedItemBackgroundProperty =
        DependencyProperty.Register(
            nameof(SuppressSelectedItemBackground),
            typeof(bool),
            typeof(HomeLaunchGameListView),
            new PropertyMetadata(false));

    private const double FallbackPanelWidth = 224;
    private const double FallbackCollapsedHeight = 72;
    private const double FallbackItemHeight = 54;
    private const double FallbackAnimationDurationMilliseconds = 320;
    private const double FallbackAnimationEasePower = 2.4;
    private static readonly Thickness FallbackPanelMargin = new(24, 24, 0, 24);

    private HomeLaunchGameListViewModel? attachedViewModel;
    private bool isApplyQueued;
    private bool isPointerExpanded;
    private bool pendingAnimate;
    private int animationGeneration;
    private int measureRetryCount;

    public HomeLaunchGameListView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => QueueApplyMenuState(animate: IsLoaded);
        HomeLaunchMenuPanelShadow.MouseEnter += (_, _) => SetPointerExpanded(true);
        HomeLaunchMenuPanelShadow.MouseLeave += (_, _) => SetPointerExpanded(false);
    }

    internal FrameworkElement FloatingLayerElement => HomeLaunchFloatingLayer;

    internal FrameworkElement MenuPanelShadowElement => HomeLaunchMenuPanelShadow;

    internal FrameworkElement HeaderOverlayElement => HomeLaunchHeaderOverlay;

    internal ToggleButton PinButtonElement => HomeLaunchMenuPinButton;

    internal FrameworkElement MenuViewportElement => HomeLaunchMenuViewport;

    internal ListBox LaunchInstanceListBox => HomeLaunchInstanceListBox;

    internal TranslateTransform ListTranslateTransform => HomeLaunchListTranslate;

    internal bool IsMenuExpanded => ShouldUseExpandedState();

    internal bool IsSelectedItemBackgroundSuppressed => SuppressSelectedItemBackground;

    internal double CollapsedMenuHeight => GetResourceDouble("HomeLaunchMenuCollapsedHeight", FallbackCollapsedHeight);

    public bool SuppressSelectedItemBackground
    {
        get => (bool)GetValue(SuppressSelectedItemBackgroundProperty);
        set => SetValue(SuppressSelectedItemBackgroundProperty, value);
    }

    internal void SetPointerExpandedForTest(bool value)
    {
        SetPointerExpanded(value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as HomeLaunchGameListViewModel);
        HomeLaunchMenuPanelShadow.Width = GetResourceDouble("HomeLaunchMenuPanelWidth", FallbackPanelWidth);
        HomeLaunchMenuPanelShadow.Height = GetCollapsedHeight();
        HomeLaunchMenuPanelShadow.Margin = GetPanelMargin();
        QueueApplyMenuState(animate: false, DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel(attachedViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel(e.OldValue as HomeLaunchGameListViewModel);
        AttachViewModel(e.NewValue as HomeLaunchGameListViewModel);
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void AttachViewModel(HomeLaunchGameListViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(attachedViewModel, viewModel))
            return;

        attachedViewModel = viewModel;
        viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        viewModel.LaunchInstances.CollectionChanged += LaunchInstances_OnCollectionChanged;
    }

    private void DetachViewModel(HomeLaunchGameListViewModel? viewModel)
    {
        if (viewModel is null || !ReferenceEquals(attachedViewModel, viewModel))
            return;

        viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        viewModel.LaunchInstances.CollectionChanged -= LaunchInstances_OnCollectionChanged;
        attachedViewModel = null;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeLaunchGameListViewModel.SelectedLaunchInstanceItem)
            or nameof(HomeLaunchGameListViewModel.HasSelectedLaunchInstance)
            or nameof(HomeLaunchGameListViewModel.HasLaunchInstances)
            or nameof(HomeLaunchGameListViewModel.HasNoLaunchInstances)
            or nameof(HomeLaunchGameListViewModel.IsLaunchMenuPinned))
        {
            measureRetryCount = 0;
            QueueApplyMenuState(animate: IsLoaded);
        }
    }

    private void LaunchInstances_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        measureRetryCount = 0;
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void SetPointerExpanded(bool expanded)
    {
        if (isPointerExpanded == expanded)
            return;

        isPointerExpanded = expanded;
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void QueueApplyMenuState(bool animate, DispatcherPriority priority = DispatcherPriority.Background)
    {
        pendingAnimate |= animate;
        if (isApplyQueued || !Dispatcher.CheckAccess())
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => QueueApplyMenuState(animate, priority), priority);
            }

            return;
        }

        isApplyQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isApplyQueued = false;
                var animateNow = pendingAnimate;
                pendingAnimate = false;
                ApplyMenuState(animateNow);
            },
            priority);
    }

    private void ApplyMenuState(bool animate)
    {
        var expandedHeight = GetExpandedHeight();
        HomeLaunchMenuViewport.Height = expandedHeight;

        var shouldExpand = ShouldUseExpandedState();
        SuppressSelectedItemBackground = !shouldExpand;
        HomeLaunchHeaderOverlay.IsHitTestVisible = shouldExpand;
        if (!shouldExpand
            && attachedViewModel?.SelectedLaunchInstanceItem is not null
            && !PrepareSelectedItemForMeasurement())
        {
            if (measureRetryCount++ < 4)
            {
                QueueApplyMenuState(animate, DispatcherPriority.ApplicationIdle);
                return;
            }
        }
        else
        {
            measureRetryCount = 0;
        }

        var generation = ++animationGeneration;
        var targetHeight = shouldExpand ? expandedHeight : GetCollapsedHeight();
        var targetTranslate = shouldExpand ? 0 : CalculateCollapsedListTranslate();
        var targetHeaderOpacity = shouldExpand ? 1 : 0;

        AnimateDouble(HomeLaunchMenuPanelShadow, HeightProperty, targetHeight, animate, generation);
        AnimateDouble(HomeLaunchListTranslate, TranslateTransform.YProperty, targetTranslate, animate, generation);
        AnimateDouble(HomeLaunchHeaderOverlay, OpacityProperty, targetHeaderOpacity, animate, generation);
    }

    private bool ShouldUseExpandedState()
    {
        if (!CanUseCollapsedState())
            return true;

        if (attachedViewModel?.IsLaunchMenuPinned == true)
            return true;

        return isPointerExpanded && attachedViewModel?.HasLaunchInstances == true;
    }

    private bool CanUseCollapsedState()
    {
        return attachedViewModel?.SelectedLaunchInstanceItem is not null;
    }

    private bool PrepareSelectedItemForMeasurement()
    {
        var selectedItem = attachedViewModel?.SelectedLaunchInstanceItem;
        if (selectedItem is null)
            return false;

        HomeLaunchInstanceListBox.ApplyTemplate();
        HomeLaunchInstanceListBox.UpdateLayout();

        if (GetSelectedItemContainer(selectedItem) is { ActualHeight: > 0 })
            return true;

        HomeLaunchInstanceListBox.ScrollIntoView(selectedItem);
        HomeLaunchInstanceListBox.UpdateLayout();
        return GetSelectedItemContainer(selectedItem) is { ActualHeight: > 0 };
    }

    private double CalculateCollapsedListTranslate()
    {
        var selectedItem = attachedViewModel?.SelectedLaunchInstanceItem;
        var container = selectedItem is null ? null : GetSelectedItemContainer(selectedItem);
        if (container is null)
            return 0;

        try
        {
            var currentTop = container
                .TransformToAncestor(HomeLaunchMenuPanel)
                .Transform(new Point(0, 0))
                .Y;
            var baseTop = currentTop - HomeLaunchListTranslate.Y;
            var itemHeight = container.ActualHeight > 0 ? container.ActualHeight : GetItemHeight();
            var slotTop = Math.Max(0, (GetCollapsedHeight() - itemHeight) / 2);
            return slotTop - baseTop;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private FrameworkElement? GetSelectedItemContainer(HomeLaunchInstanceItem selectedItem)
    {
        return HomeLaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem) as FrameworkElement;
    }

    private void AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double to,
        bool animate,
        int generation)
    {
        if (target is not IAnimatable animatable)
        {
            target.SetValue(property, to);
            return;
        }

        var from = GetCurrentDouble(target, property);
        animatable.BeginAnimation(property, null);
        target.SetValue(property, from);

        if (!animate || Math.Abs(from - to) < 0.1)
        {
            target.SetValue(property, to);
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = GetAnimationDuration(),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = CreateAnimationEasing()
        };
        animation.Completed += (_, _) =>
        {
            if (generation != animationGeneration)
                return;

            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
        };

        animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double GetCurrentDouble(DependencyObject target, DependencyProperty property)
    {
        var value = (double)target.GetValue(property);
        if (!double.IsNaN(value))
            return value;

        return target is FrameworkElement element ? element.ActualHeight : 0;
    }

    private double GetExpandedHeight()
    {
        var margin = GetPanelMargin();
        var layerHeight = HomeLaunchFloatingLayer.ActualHeight > 0
            ? HomeLaunchFloatingLayer.ActualHeight
            : ActualHeight;
        var expandedHeight = layerHeight - margin.Top - margin.Bottom;
        return Math.Max(GetCollapsedHeight(), expandedHeight);
    }

    private double GetCollapsedHeight()
    {
        return GetResourceDouble("HomeLaunchMenuCollapsedHeight", FallbackCollapsedHeight);
    }

    private double GetItemHeight()
    {
        return GetResourceDouble("HomeLaunchMenuItemHeight", FallbackItemHeight);
    }

    private Duration GetAnimationDuration()
    {
        return new Duration(TimeSpan.FromMilliseconds(GetResourceDouble(
            "HomeLaunchMenuAnimationDurationMilliseconds",
            FallbackAnimationDurationMilliseconds)));
    }

    private IEasingFunction CreateAnimationEasing()
    {
        return new PowerEase
        {
            Power = GetResourceDouble("HomeLaunchMenuAnimationEasePower", FallbackAnimationEasePower),
            EasingMode = EasingMode.EaseOut
        };
    }

    private Thickness GetPanelMargin()
    {
        return TryFindResource("HomeLaunchMenuPanelMargin") is Thickness margin
            ? margin
            : FallbackPanelMargin;
    }

    private double GetResourceDouble(string key, double fallback)
    {
        return TryFindResource(key) is double value ? value : fallback;
    }
}
