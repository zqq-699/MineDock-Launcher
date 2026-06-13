using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

    private static readonly DependencyProperty AnimatedScrollOffsetProperty =
        DependencyProperty.Register(
            nameof(AnimatedScrollOffset),
            typeof(double),
            typeof(DownloadPageView),
            new PropertyMetadata(0d, OnAnimatedScrollOffsetChanged));

    private INotifyPropertyChanged? currentViewModelNotifier;
    private DownloadVersionListView? downloadVersionListView;
    private Button? scrollToSelectedVersionButton;
    private bool isScrollToSelectedVisibilityUpdateQueued;
    private bool isScrollToSelectedButtonShown;

    public DownloadPageView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            QueueScrollToSelectedButtonVisibilityUpdate();
        };
        DataContextChanged += DownloadPageView_OnDataContextChanged;
        SizeChanged += (_, _) =>
        {
            QueueScrollToSelectedButtonVisibilityUpdate();
        };
        VersionScrollViewer.ScrollChanged += (_, _) =>
        {
            QueueScrollToSelectedButtonVisibilityUpdate();
        };
    }

    public FrameworkElement RootElement => PageRoot;

    private ScrollViewer VersionScrollViewer => DownloadVersionListFrame.ScrollViewer;

    private DownloadVersionListView DownloadVersionList =>
        downloadVersionListView ??= DownloadVersionListFrame.ListContent as DownloadVersionListView
            ?? throw new InvalidOperationException("Download version list content is not a DownloadVersionListView.");

    private Button ScrollToSelectedVersionButton =>
        scrollToSelectedVersionButton ??= FindFloatingButton("ScrollToSelectedVersion");

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

    private void DownloadPageView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= DownloadPageViewModel_OnPropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += DownloadPageViewModel_OnPropertyChanged;

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
    }

    private void ResetVersionScrollPosition()
    {
        BeginAnimation(AnimatedScrollOffsetProperty, null);
        AnimatedScrollOffset = 0;
        VersionScrollViewer.ScrollToVerticalOffset(0);
        QueueScrollToSelectedButtonVisibilityUpdate();
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
        if (DataContext is not DownloadPageViewModel { SelectedMinecraftVersion: { } selectedVersion, HasVisibleVersions: true })
            return false;

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
        if (DataContext is not DownloadPageViewModel { SelectedMinecraftVersion: { } selectedVersion })
            return;

        var selectedButton = DownloadVersionList.IsVersionRendered(selectedVersion)
            ? FindVersionButton(selectedVersion)
            : null;
        var targetOffset = selectedButton is not null && GetElementBoundsInScrollViewer(selectedButton) is { } bounds
            ? VersionScrollViewer.VerticalOffset
                + bounds.Top
                - GetUsableViewportTop()
                - Math.Max(0, (GetUsableViewportHeight() - selectedButton.ActualHeight) / 2)
            : GetUsableViewportTop()
                + DownloadVersionList.GetVersionTopOffset(selectedVersion)
                - Math.Max(0, (GetUsableViewportHeight() - DownloadVersionList.EstimatedVersionItemHeight) / 2);

        AnimateVersionListScrollTo(Math.Clamp(targetOffset, 0, VersionScrollViewer.ScrollableHeight));
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
