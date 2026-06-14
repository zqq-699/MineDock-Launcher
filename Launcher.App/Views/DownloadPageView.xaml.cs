using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class DownloadPageView : UserControl
{
    private const double FarScrollAnimationThreshold = 900d;
    private const double FarScrollAnimationTailDistance = 420d;
    private static readonly TimeSpan NormalScrollAnimationDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan FarScrollTailAnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan StepTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan StepButtonFadeDuration = TimeSpan.FromMilliseconds(180);

    private static readonly DependencyProperty AnimatedScrollOffsetProperty =
        DependencyProperty.Register(
            nameof(AnimatedScrollOffset),
            typeof(double),
            typeof(DownloadPageView),
            new PropertyMetadata(0d, OnAnimatedScrollOffsetChanged));

    private INotifyPropertyChanged? currentViewModelNotifier;
    private FrameworkElement? downloadStepHost;
    private FrameworkElement? versionListStepLayer;
    private FrameworkElement? instanceOptionsStepLayer;
    private DownloadVersionListView? downloadVersionListView;
    private ScrollViewer? hookedVersionScrollViewer;
    private Button? scrollToSelectedVersionButton;
    private Button? nextStepButton;
    private Button? previousStepButton;
    private Button? installStepButton;
    private bool isScrollToSelectedVisibilityUpdateQueued;
    private bool isScrollToSelectedButtonShown;
    private DownloadPageStep displayedStep = DownloadPageStep.VersionList;
    private int stepTransitionToken;

    public DownloadPageView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SyncStepLayers(GetCurrentStep());
            AttachVersionScrollViewer();
            QueueScrollToSelectedButtonVisibilityUpdate();
        };
        Unloaded += (_, _) => DetachVersionScrollViewer();
        DataContextChanged += DownloadPageView_OnDataContextChanged;
        SizeChanged += (_, _) =>
        {
            QueueScrollToSelectedButtonVisibilityUpdate();
        };
    }

    public FrameworkElement RootElement => PageRoot;

    private ScrollViewer VersionScrollViewer => DownloadVersionList.ScrollViewer;

    private FrameworkElement DownloadStepHost =>
        downloadStepHost ??= DownloadVersionListFrame.ListContent as FrameworkElement
            ?? throw new InvalidOperationException("Download step host content is not available.");

    private FrameworkElement VersionListStepLayer =>
        versionListStepLayer ??= FindStepElement("VersionListStep");

    private FrameworkElement InstanceOptionsStepLayer =>
        instanceOptionsStepLayer ??= FindStepElement("InstanceOptionsStep");

    private DownloadVersionListView DownloadVersionList =>
        downloadVersionListView ??= VisualTreeSearch.FindDescendant<DownloadVersionListView>(
            DownloadStepHost,
            element => Equals(element.Tag, "DownloadVersionList"))
            ?? throw new InvalidOperationException("Download version list view was not found.");

    private FrameworkElement FindStepElement(string tag)
    {
        return VisualTreeSearch.FindDescendant<FrameworkElement>(
            DownloadStepHost,
            element => Equals(element.Tag, tag))
            ?? throw new InvalidOperationException($"Download step layer '{tag}' was not found.");
    }

    private Button ScrollToSelectedVersionButton =>
        scrollToSelectedVersionButton ??= FindFloatingButton("ScrollToSelectedVersion");

    private Button NextStepButton =>
        nextStepButton ??= FindFloatingButton("NextStep");

    private Button PreviousStepButton =>
        previousStepButton ??= FindFloatingButton("PreviousStep");

    private Button InstallStepButton =>
        installStepButton ??= FindFloatingButton("InstallStep");

    private Button FindFloatingButton(string tag)
    {
        if (DownloadVersionListFrame.FloatingContent is not DependencyObject floatingContent)
            throw new InvalidOperationException("Download version floating content is not available.");

        return VisualTreeSearch.FindDescendant<Button>(floatingContent, button => Equals(button.Tag, tag))
            ?? throw new InvalidOperationException($"Download version floating button '{tag}' was not found.");
    }

    private double AnimatedScrollOffset
    {
        get => (double)GetValue(AnimatedScrollOffsetProperty);
        set => SetValue(AnimatedScrollOffsetProperty, value);
    }

    private void AttachVersionScrollViewer()
    {
        var nextScrollViewer = VersionScrollViewer;
        if (ReferenceEquals(hookedVersionScrollViewer, nextScrollViewer))
            return;

        DetachVersionScrollViewer();
        hookedVersionScrollViewer = nextScrollViewer;
        hookedVersionScrollViewer.ScrollChanged += VersionScrollViewer_OnScrollChanged;
    }

    private void DetachVersionScrollViewer()
    {
        if (hookedVersionScrollViewer is null)
            return;

        hookedVersionScrollViewer.ScrollChanged -= VersionScrollViewer_OnScrollChanged;
        hookedVersionScrollViewer = null;
    }

    private void VersionScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueScrollToSelectedButtonVisibilityUpdate();
    }

    private void DownloadPageView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= DownloadPageViewModel_OnPropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += DownloadPageViewModel_OnPropertyChanged;

        SyncStepLayers(GetCurrentStep());
        QueueScrollToSelectedButtonVisibilityUpdate();
    }

    private void DownloadPageViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadPageViewModel.SelectedMinecraftVersion)
            or nameof(DownloadPageViewModel.VisibleVersions)
            or nameof(DownloadPageViewModel.HasVisibleVersions))
        {
            QueueScrollToSelectedButtonVisibilityUpdate();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.SelectedVersionCategory)
            or nameof(DownloadPageViewModel.VersionSearchQuery))
        {
            ResetVersionScrollPosition();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.ContentRefreshToken))
        {
            RefreshRightContentView();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.CurrentStep)
            && sender is DownloadPageViewModel viewModel)
        {
            AnimateStepTransition(viewModel.CurrentStep);
            QueueScrollToSelectedButtonVisibilityUpdate();
        }
    }

    private void ResetVersionScrollPosition()
    {
        BeginAnimation(AnimatedScrollOffsetProperty, null);
        AnimatedScrollOffset = 0;
        VersionScrollViewer.ScrollToVerticalOffset(0);
        QueueScrollToSelectedButtonVisibilityUpdate();
    }

    private void RefreshRightContentView()
    {
        SyncStepLayers(GetCurrentStep());
        ResetVersionScrollPosition();
        DownloadVersionList.RefreshViewport();
    }

    private void SecondaryMenuOptionButton_OnRefreshRequested(object sender, RoutedEventArgs e)
    {
        RefreshRightContentView();
    }

    private void QueueScrollToSelectedButtonVisibilityUpdate()
    {
        if (isScrollToSelectedVisibilityUpdateQueued)
            return;

        isScrollToSelectedVisibilityUpdateQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isScrollToSelectedVisibilityUpdateQueued = false;
                UpdateScrollToSelectedButtonVisibility();
            },
            DispatcherPriority.Background);
    }

    private void UpdateScrollToSelectedButtonVisibility()
    {
        SetScrollToSelectedButtonVisible(IsSelectedVersionOutsideViewport());
    }

    private void SetScrollToSelectedButtonVisible(bool shouldShow)
    {
        if (isScrollToSelectedButtonShown == shouldShow)
            return;

        isScrollToSelectedButtonShown = shouldShow;
        ScrollToSelectedVersionButton.BeginAnimation(OpacityProperty, null);

        var animation = new DoubleAnimation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (shouldShow)
        {
            ScrollToSelectedVersionButton.Visibility = Visibility.Visible;
            animation.From = ScrollToSelectedVersionButton.Opacity;
            animation.To = 1;
            ScrollToSelectedVersionButton.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        animation.From = ScrollToSelectedVersionButton.Opacity;
        animation.To = 0;
        animation.Completed += (_, _) =>
        {
            if (!isScrollToSelectedButtonShown)
                ScrollToSelectedVersionButton.Visibility = Visibility.Collapsed;
        };
        ScrollToSelectedVersionButton.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private bool IsSelectedVersionOutsideViewport()
    {
        if (DataContext is not DownloadPageViewModel
            {
                IsVersionListStep: true,
                SelectedMinecraftVersion: { } selectedVersion,
                HasVisibleVersions: true
            })
        {
            return false;
        }

        if (!DownloadVersionList.IsVersionRendered(selectedVersion))
            return DownloadVersionList.ContainsVersion(selectedVersion);

        var selectedButton = FindVersionButton(selectedVersion);
        if (selectedButton is null || !selectedButton.IsVisible || selectedButton.ActualHeight <= 0)
            return true;

        var bounds = GetElementBoundsInScrollViewer(selectedButton);
        if (bounds is null)
            return false;

        var visibleTop = GetUsableViewportTop();
        var visibleBottom = Math.Max(visibleTop, VersionScrollViewer.ActualHeight - 72);
        return bounds.Value.Top < visibleTop || bounds.Value.Bottom > visibleBottom;
    }

    private void ScrollToSelectedVersionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ScrollToSelectedVersion();
    }

    private void ScrollToSelectedVersion()
    {
        if (DataContext is not DownloadPageViewModel { SelectedMinecraftVersion: { } selectedVersion })
            return;

        if (!TryGetSelectedVersionTargetOffset(selectedVersion, out var targetOffset))
            return;

        AnimateVersionListScrollTo(Math.Clamp(targetOffset, 0, VersionScrollViewer.ScrollableHeight));
    }

    private bool TryGetSelectedVersionTargetOffset(DownloadMinecraftVersionItem selectedVersion, out double targetOffset)
    {
        if (TryGetRenderedVersionTargetOffset(selectedVersion, out targetOffset))
            return true;

        var originalOffset = VersionScrollViewer.VerticalOffset;
        try
        {
            if (DownloadVersionList.RealizeVersion(selectedVersion)
                && TryGetRenderedVersionTargetOffset(selectedVersion, out targetOffset))
            {
                return true;
            }
        }
        finally
        {
            if (Math.Abs(VersionScrollViewer.VerticalOffset - originalOffset) > 0.1)
            {
                VersionScrollViewer.ScrollToVerticalOffset(originalOffset);
                DownloadVersionList.RefreshViewport();
            }
        }

        return TryGetEstimatedVersionTargetOffset(selectedVersion, out targetOffset);
    }

    private bool TryGetRenderedVersionTargetOffset(DownloadMinecraftVersionItem selectedVersion, out double targetOffset)
    {
        targetOffset = 0;

        var selectedButton = DownloadVersionList.IsVersionRendered(selectedVersion)
            ? FindVersionButton(selectedVersion)
            : null;
        if (selectedButton is null || GetElementBoundsInScrollViewer(selectedButton) is not { } bounds)
            return false;

        targetOffset = VersionScrollViewer.VerticalOffset
            + bounds.Top
            - GetUsableViewportTop()
            - Math.Max(0, (GetUsableViewportHeight() - selectedButton.ActualHeight) / 2);
        return true;
    }

    private bool TryGetEstimatedVersionTargetOffset(DownloadMinecraftVersionItem selectedVersion, out double targetOffset)
    {
        targetOffset = 0;
        if (!DownloadVersionList.ContainsVersion(selectedVersion))
            return false;

        targetOffset = DownloadVersionList.GetVersionTopOffset(selectedVersion)
            - Math.Max(0, (GetUsableViewportHeight() - DownloadVersionList.EstimatedVersionItemHeight) / 2);
        return true;
    }

    private void AnimateVersionListScrollTo(double targetOffset)
    {
        BeginAnimation(AnimatedScrollOffsetProperty, null);
        var currentOffset = VersionScrollViewer.VerticalOffset;
        var distance = targetOffset - currentOffset;
        if (Math.Abs(distance) > FarScrollAnimationThreshold)
        {
            var direction = Math.Sign(distance);
            currentOffset = Math.Clamp(targetOffset - direction * FarScrollAnimationTailDistance, 0, VersionScrollViewer.ScrollableHeight);
            AnimatedScrollOffset = currentOffset;
            VersionScrollViewer.ScrollToVerticalOffset(currentOffset);
            DownloadVersionList.RefreshViewport();
            Dispatcher.BeginInvoke(
                () => BeginVersionListScrollAnimation(currentOffset, targetOffset, FarScrollTailAnimationDuration),
                DispatcherPriority.Render);
            return;
        }

        AnimatedScrollOffset = currentOffset;
        BeginVersionListScrollAnimation(currentOffset, targetOffset, NormalScrollAnimationDuration);
    }

    private void BeginVersionListScrollAnimation(double fromOffset, double targetOffset, TimeSpan duration)
    {
        var animation = new DoubleAnimation(fromOffset, targetOffset, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            BeginAnimation(AnimatedScrollOffsetProperty, null);
            AnimatedScrollOffset = targetOffset;
            VersionScrollViewer.ScrollToVerticalOffset(targetOffset);
            QueueScrollToSelectedButtonVisibilityUpdate();
        };

        BeginAnimation(AnimatedScrollOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnAnimatedScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DownloadPageView view && e.NewValue is double offset)
            view.VersionScrollViewer.ScrollToVerticalOffset(offset);
    }

    private DownloadPageStep GetCurrentStep()
    {
        return DataContext is DownloadPageViewModel viewModel
            ? viewModel.CurrentStep
            : DownloadPageStep.VersionList;
    }

    private void SyncStepLayers(DownloadPageStep step)
    {
        stepTransitionToken++;
        displayedStep = step;

        var versionLayer = VersionListStepLayer;
        var optionsLayer = InstanceOptionsStepLayer;
        ResetStepLayer(versionLayer, step is DownloadPageStep.VersionList);
        ResetStepLayer(optionsLayer, step is DownloadPageStep.InstanceOptions);
        SyncStepButtons(step);
    }

    private static void ResetStepLayer(FrameworkElement layer, bool isVisible)
    {
        layer.BeginAnimation(OpacityProperty, null);
        var transform = EnsureStepTransform(layer);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        layer.Opacity = isVisible ? 1 : 0;
        layer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimateStepTransition(DownloadPageStep nextStep)
    {
        if (displayedStep == nextStep)
        {
            SyncStepLayers(nextStep);
            return;
        }

        if (!IsLoaded || DownloadStepHost.ActualWidth <= 0)
        {
            SyncStepLayers(nextStep);
            return;
        }

        var oldStep = displayedStep;
        displayedStep = nextStep;
        var token = ++stepTransitionToken;
        var width = Math.Max(DownloadStepHost.ActualWidth, 1);
        var direction = nextStep is DownloadPageStep.InstanceOptions ? 1 : -1;
        var oldLayer = GetStepLayer(oldStep);
        var nextLayer = GetStepLayer(nextStep);
        var oldTransform = EnsureStepTransform(oldLayer);
        var nextTransform = EnsureStepTransform(nextLayer);

        oldLayer.Visibility = Visibility.Visible;
        oldLayer.Opacity = 1;
        oldTransform.BeginAnimation(TranslateTransform.XProperty, null);
        oldTransform.X = 0;

        nextLayer.Visibility = Visibility.Visible;
        nextLayer.Opacity = 0;
        nextTransform.BeginAnimation(TranslateTransform.XProperty, null);
        nextTransform.X = width * direction;
        AnimateStepButtons(nextStep, token);

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var oldSlide = CreateStepAnimation(0, -width * direction, easing);
        var newSlide = CreateStepAnimation(width * direction, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        var oldFade = CreateStepAnimation(1, 0, new CubicEase { EasingMode = EasingMode.EaseOut });
        var newFade = CreateStepAnimation(0, 1, new CubicEase { EasingMode = EasingMode.EaseOut });

        newSlide.Completed += (_, _) =>
        {
            if (token != stepTransitionToken)
                return;

            ResetStepLayer(oldLayer, isVisible: false);
            ResetStepLayer(nextLayer, isVisible: true);
        };

        oldLayer.BeginAnimation(OpacityProperty, oldFade, HandoffBehavior.SnapshotAndReplace);
        oldTransform.BeginAnimation(TranslateTransform.XProperty, oldSlide, HandoffBehavior.SnapshotAndReplace);
        nextLayer.BeginAnimation(OpacityProperty, newFade, HandoffBehavior.SnapshotAndReplace);
        nextTransform.BeginAnimation(TranslateTransform.XProperty, newSlide, HandoffBehavior.SnapshotAndReplace);
    }

    private FrameworkElement GetStepLayer(DownloadPageStep step)
    {
        return step is DownloadPageStep.InstanceOptions
            ? InstanceOptionsStepLayer
            : VersionListStepLayer;
    }

    private void SyncStepButtons(DownloadPageStep step)
    {
        var isInstanceOptionsStep = step is DownloadPageStep.InstanceOptions;
        ResetStepButton(NextStepButton, !isInstanceOptionsStep);
        ResetStepButton(PreviousStepButton, isInstanceOptionsStep);
        ResetStepButton(InstallStepButton, isInstanceOptionsStep);
    }

    private static void ResetStepButton(Button button, bool isVisible)
    {
        button.BeginAnimation(OpacityProperty, null);
        button.Opacity = isVisible ? 1 : 0;
        button.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        button.IsHitTestVisible = isVisible;
    }

    private void AnimateStepButtons(DownloadPageStep nextStep, int token)
    {
        if (nextStep is DownloadPageStep.InstanceOptions)
        {
            FadeStepButtonOut(NextStepButton, token);
            FadeStepButtonIn(PreviousStepButton, token);
            FadeStepButtonIn(InstallStepButton, token);
            return;
        }

        FadeStepButtonIn(NextStepButton, token);
        FadeStepButtonOut(PreviousStepButton, token);
        FadeStepButtonOut(InstallStepButton, token);
    }

    private void FadeStepButtonIn(Button button, int token)
    {
        button.BeginAnimation(OpacityProperty, null);
        button.Visibility = Visibility.Visible;
        button.IsHitTestVisible = true;

        var animation = CreateStepButtonFadeAnimation(button.Opacity, 1);
        animation.Completed += (_, _) =>
        {
            if (token != stepTransitionToken)
                return;

            button.BeginAnimation(OpacityProperty, null);
            button.Opacity = 1;
            button.Visibility = Visibility.Visible;
            button.IsHitTestVisible = true;
        };
        button.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void FadeStepButtonOut(Button button, int token)
    {
        button.BeginAnimation(OpacityProperty, null);
        button.IsHitTestVisible = false;

        var animation = CreateStepButtonFadeAnimation(button.Opacity, 0);
        animation.Completed += (_, _) =>
        {
            if (token != stepTransitionToken)
                return;

            button.BeginAnimation(OpacityProperty, null);
            button.Opacity = 0;
            button.Visibility = Visibility.Collapsed;
            button.IsHitTestVisible = false;
        };
        button.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateStepButtonFadeAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, StepButtonFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private static DoubleAnimation CreateStepAnimation(double from, double to, IEasingFunction easing)
    {
        return new DoubleAnimation(from, to, StepTransitionDuration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
    }

    private static TranslateTransform EnsureStepTransform(FrameworkElement layer)
    {
        if (layer.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        layer.RenderTransform = transform;
        return transform;
    }

    private Button? FindVersionButton(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionList.FindVersionButton(selectedVersion);
    }

    private Rect? GetElementBoundsInScrollViewer(FrameworkElement element)
    {
        try
        {
            return element
                .TransformToAncestor(VersionScrollViewer)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static double GetUsableViewportTop()
    {
        return 132;
    }

    private double GetUsableViewportHeight()
    {
        var visibleTop = GetUsableViewportTop();
        var visibleBottom = Math.Max(visibleTop, VersionScrollViewer.ActualHeight - 72);
        return Math.Max(0, visibleBottom - visibleTop);
    }
}
