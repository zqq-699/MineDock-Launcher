using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.App.Services;

public sealed class DownloadSelectedVersionScrollCoordinator : FrameworkElement
{
    private const double FarScrollAnimationThreshold = 900d;
    private const double FarScrollAnimationTailDistance = 420d;
    private const double ViewportTopOffset = 132d;
    private const double ViewportBottomOffset = 72d;

    private static readonly TimeSpan NormalScrollAnimationDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan FarScrollTailAnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan ButtonFadeDuration = TimeSpan.FromMilliseconds(180);

    private static readonly DependencyProperty AnimatedScrollOffsetProperty =
        DependencyProperty.Register(
            nameof(AnimatedScrollOffset),
            typeof(double),
            typeof(DownloadSelectedVersionScrollCoordinator),
            new PropertyMetadata(0d, OnAnimatedScrollOffsetChanged));

    private readonly DownloadVersionListView versionList;
    private readonly Button scrollToSelectedButton;
    private readonly Func<DownloadPageViewModel?> getViewModel;
    private ScrollViewer? hookedScrollViewer;
    private bool isButtonVisibilityUpdateQueued;
    private bool isButtonShown;

    public DownloadSelectedVersionScrollCoordinator(
        DownloadVersionListView versionList,
        Button scrollToSelectedButton,
        Func<DownloadPageViewModel?> getViewModel)
    {
        this.versionList = versionList;
        this.scrollToSelectedButton = scrollToSelectedButton;
        this.getViewModel = getViewModel;
    }

    private ScrollViewer ScrollViewer => versionList.ScrollViewer;

    private double AnimatedScrollOffset
    {
        get => (double)GetValue(AnimatedScrollOffsetProperty);
        set => SetValue(AnimatedScrollOffsetProperty, value);
    }

    public void Attach()
    {
        var nextScrollViewer = ScrollViewer;
        if (ReferenceEquals(hookedScrollViewer, nextScrollViewer))
            return;

        Detach();
        hookedScrollViewer = nextScrollViewer;
        hookedScrollViewer.ScrollChanged += ScrollViewer_OnScrollChanged;
    }

    public void Detach()
    {
        if (hookedScrollViewer is null)
            return;

        hookedScrollViewer.ScrollChanged -= ScrollViewer_OnScrollChanged;
        hookedScrollViewer = null;
    }

    public void ResetScrollPosition()
    {
        BeginAnimation(AnimatedScrollOffsetProperty, null);
        AnimatedScrollOffset = 0;
        ScrollViewer.ScrollToVerticalOffset(0);
        QueueButtonVisibilityUpdate();
    }

    public void QueueButtonVisibilityUpdate()
    {
        if (isButtonVisibilityUpdateQueued)
            return;

        isButtonVisibilityUpdateQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isButtonVisibilityUpdateQueued = false;
                UpdateButtonVisibility();
            },
            DispatcherPriority.Background);
    }

    public void ScrollToSelectedVersion()
    {
        if (getViewModel() is not { SelectedMinecraftVersion: { } selectedVersion })
            return;

        if (!TryGetSelectedVersionTargetOffset(selectedVersion, out var targetOffset))
            return;

        AnimateScrollTo(Math.Clamp(targetOffset, 0, ScrollViewer.ScrollableHeight));
    }

    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueButtonVisibilityUpdate();
    }

    private void UpdateButtonVisibility()
    {
        SetButtonVisible(IsSelectedVersionOutsideViewport());
    }

    private void SetButtonVisible(bool shouldShow)
    {
        if (isButtonShown == shouldShow)
            return;

        isButtonShown = shouldShow;
        scrollToSelectedButton.BeginAnimation(OpacityProperty, null);

        var animation = new DoubleAnimation
        {
            Duration = ButtonFadeDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (shouldShow)
        {
            scrollToSelectedButton.Visibility = Visibility.Visible;
            animation.From = scrollToSelectedButton.Opacity;
            animation.To = 1;
            scrollToSelectedButton.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        animation.From = scrollToSelectedButton.Opacity;
        animation.To = 0;
        animation.Completed += (_, _) =>
        {
            if (!isButtonShown)
                scrollToSelectedButton.Visibility = Visibility.Collapsed;
        };
        scrollToSelectedButton.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private bool IsSelectedVersionOutsideViewport()
    {
        if (getViewModel() is not
            {
                IsVersionListStep: true,
                SelectedMinecraftVersion: { } selectedVersion,
                HasVisibleVersions: true
            })
        {
            return false;
        }

        if (!versionList.IsVersionRendered(selectedVersion))
            return versionList.ContainsVersion(selectedVersion);

        var selectedButton = versionList.FindVersionButton(selectedVersion);
        if (selectedButton is null || !selectedButton.IsVisible || selectedButton.ActualHeight <= 0)
            return true;

        var bounds = GetElementBoundsInScrollViewer(selectedButton);
        if (bounds is null)
            return false;

        var visibleTop = ViewportTopOffset;
        var visibleBottom = Math.Max(visibleTop, ScrollViewer.ActualHeight - ViewportBottomOffset);
        return bounds.Value.Top < visibleTop || bounds.Value.Bottom > visibleBottom;
    }

    private bool TryGetSelectedVersionTargetOffset(DownloadMinecraftVersionItem selectedVersion, out double targetOffset)
    {
        if (TryGetRenderedVersionTargetOffset(selectedVersion, out targetOffset))
            return true;

        var originalOffset = ScrollViewer.VerticalOffset;
        try
        {
            if (versionList.RealizeVersion(selectedVersion)
                && TryGetRenderedVersionTargetOffset(selectedVersion, out targetOffset))
            {
                return true;
            }
        }
        finally
        {
            if (Math.Abs(ScrollViewer.VerticalOffset - originalOffset) > 0.1)
            {
                ScrollViewer.ScrollToVerticalOffset(originalOffset);
                versionList.RefreshViewport();
            }
        }

        return TryGetEstimatedVersionTargetOffset(selectedVersion, out targetOffset);
    }

    private bool TryGetRenderedVersionTargetOffset(DownloadMinecraftVersionItem selectedVersion, out double targetOffset)
    {
        targetOffset = 0;

        var selectedButton = versionList.IsVersionRendered(selectedVersion)
            ? versionList.FindVersionButton(selectedVersion)
            : null;
        if (selectedButton is null || GetElementBoundsInScrollViewer(selectedButton) is not { } bounds)
            return false;

        targetOffset = ScrollViewer.VerticalOffset
            + bounds.Top
            - ViewportTopOffset
            - Math.Max(0, (GetUsableViewportHeight() - selectedButton.ActualHeight) / 2);
        return true;
    }

    private bool TryGetEstimatedVersionTargetOffset(DownloadMinecraftVersionItem selectedVersion, out double targetOffset)
    {
        targetOffset = 0;
        if (!versionList.ContainsVersion(selectedVersion))
            return false;

        targetOffset = versionList.GetVersionTopOffset(selectedVersion)
            - Math.Max(0, (GetUsableViewportHeight() - versionList.EstimatedVersionItemHeight) / 2);
        return true;
    }

    private void AnimateScrollTo(double targetOffset)
    {
        BeginAnimation(AnimatedScrollOffsetProperty, null);
        var currentOffset = ScrollViewer.VerticalOffset;
        var distance = targetOffset - currentOffset;
        if (Math.Abs(distance) > FarScrollAnimationThreshold)
        {
            var direction = Math.Sign(distance);
            currentOffset = Math.Clamp(
                targetOffset - direction * FarScrollAnimationTailDistance,
                0,
                ScrollViewer.ScrollableHeight);
            AnimatedScrollOffset = currentOffset;
            ScrollViewer.ScrollToVerticalOffset(currentOffset);
            versionList.RefreshViewport();
            Dispatcher.BeginInvoke(
                () => BeginScrollAnimation(currentOffset, targetOffset, FarScrollTailAnimationDuration),
                DispatcherPriority.Render);
            return;
        }

        AnimatedScrollOffset = currentOffset;
        BeginScrollAnimation(currentOffset, targetOffset, NormalScrollAnimationDuration);
    }

    private void BeginScrollAnimation(double fromOffset, double targetOffset, TimeSpan duration)
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
            ScrollViewer.ScrollToVerticalOffset(targetOffset);
            QueueButtonVisibilityUpdate();
        };

        BeginAnimation(AnimatedScrollOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnAnimatedScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DownloadSelectedVersionScrollCoordinator coordinator && e.NewValue is double offset)
            coordinator.ScrollViewer.ScrollToVerticalOffset(offset);
    }

    private Rect? GetElementBoundsInScrollViewer(FrameworkElement element)
    {
        try
        {
            return element
                .TransformToAncestor(ScrollViewer)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private double GetUsableViewportHeight()
    {
        var visibleBottom = Math.Max(ViewportTopOffset, ScrollViewer.ActualHeight - ViewportBottomOffset);
        return Math.Max(0, visibleBottom - ViewportTopOffset);
    }
}
