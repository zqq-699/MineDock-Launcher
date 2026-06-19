using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Launcher.App.Behaviors;

public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ScrollAmountProperty =
        DependencyProperty.RegisterAttached(
            "ScrollAmount",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(84d));

    public static readonly DependencyProperty AllowContentScrollProperty =
        DependencyProperty.RegisterAttached(
            "AllowContentScroll",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty WheelAnimationDurationMillisecondsProperty =
        DependencyProperty.RegisterAttached(
            "WheelAnimationDurationMilliseconds",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(280d));

    private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedVerticalOffset",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(0d, OnAnimatedVerticalOffsetChanged));

    private static readonly DependencyProperty TargetVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetVerticalOffset",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty IsInternalScrollUpdateProperty =
        DependencyProperty.RegisterAttached(
            "IsInternalScrollUpdate",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimating",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty AnimationVersionProperty =
        DependencyProperty.RegisterAttached(
            "AnimationVersion",
            typeof(int),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetScrollAmount(DependencyObject element) => (double)element.GetValue(ScrollAmountProperty);

    public static void SetScrollAmount(DependencyObject element, double value) => element.SetValue(ScrollAmountProperty, value);

    public static bool GetAllowContentScroll(DependencyObject element) => (bool)element.GetValue(AllowContentScrollProperty);

    public static void SetAllowContentScroll(DependencyObject element, bool value) => element.SetValue(AllowContentScrollProperty, value);

    public static double GetWheelAnimationDurationMilliseconds(DependencyObject element) => (double)element.GetValue(WheelAnimationDurationMillisecondsProperty);

    public static void SetWheelAnimationDurationMilliseconds(DependencyObject element, double value) => element.SetValue(WheelAnimationDurationMillisecondsProperty, value);

    public static void CancelAnimation(ScrollViewer scrollViewer)
    {
        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
        SetAnimatedVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        SetIsAnimating(scrollViewer, false);
    }

    public static bool CancelAnimationFromDescendant(DependencyObject root)
    {
        if (FindDescendant<ScrollViewer>(root) is not { } scrollViewer)
            return false;

        CancelAnimation(scrollViewer);
        return true;
    }

    private static double GetAnimatedVerticalOffset(DependencyObject element) => (double)element.GetValue(AnimatedVerticalOffsetProperty);

    private static void SetAnimatedVerticalOffset(DependencyObject element, double value) => element.SetValue(AnimatedVerticalOffsetProperty, value);

    private static double GetTargetVerticalOffset(DependencyObject element) => (double)element.GetValue(TargetVerticalOffsetProperty);

    private static void SetTargetVerticalOffset(DependencyObject element, double value) => element.SetValue(TargetVerticalOffsetProperty, value);

    private static bool GetIsInternalScrollUpdate(DependencyObject element) => (bool)element.GetValue(IsInternalScrollUpdateProperty);

    private static void SetIsInternalScrollUpdate(DependencyObject element, bool value) => element.SetValue(IsInternalScrollUpdateProperty, value);

    private static bool GetIsAnimating(DependencyObject element) => (bool)element.GetValue(IsAnimatingProperty);

    private static void SetIsAnimating(DependencyObject element, bool value) => element.SetValue(IsAnimatingProperty, value);

    private static int GetAnimationVersion(DependencyObject element) => (int)element.GetValue(AnimationVersionProperty);

    private static void SetAnimationVersion(DependencyObject element, int value) => element.SetValue(AnimationVersionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            UpdateDescendantScrollHost(d, (bool)e.NewValue);
            return;
        }

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            scrollViewer.Unloaded += ScrollViewer_Unloaded;
            SetAnimatedVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
            SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
            return;
        }

        scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
        scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        scrollViewer.Unloaded -= ScrollViewer_Unloaded;
        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
        SetIsAnimating(scrollViewer, false);
    }

    private static void UpdateDescendantScrollHost(DependencyObject d, bool isEnabled)
    {
        if (d is not FrameworkElement element)
            return;

        if (isEnabled)
        {
            element.PreviewMouseWheel += DescendantScrollHost_PreviewMouseWheel;
            element.Unloaded += DescendantScrollHost_Unloaded;
            return;
        }

        element.PreviewMouseWheel -= DescendantScrollHost_PreviewMouseWheel;
        element.Unloaded -= DescendantScrollHost_Unloaded;
        CancelAnimationFromDescendant(element);
    }

    private static void DescendantScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject root)
            HandleMouseWheelFromDescendant(root, e);
    }

    private static void DescendantScrollHost_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject root)
            CancelAnimationFromDescendant(root);
    }

    private static void ScrollViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetIsAnimating(scrollViewer, false);
        }
    }

    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || GetIsInternalScrollUpdate(scrollViewer)
            || GetIsAnimating(scrollViewer))
        {
            return;
        }

        SetAnimatedVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
    }

    private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || !HandleMouseWheel(scrollViewer, e))
        {
            return;
        }
    }

    public static bool HandleMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e)
    {
        return HandleMouseWheel(scrollViewer, e, scrollViewer);
    }

    private static bool HandleMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e, DependencyObject optionsSource)
    {
        if (scrollViewer.ScrollableHeight <= 0
            || (scrollViewer.CanContentScroll && !GetAllowContentScroll(optionsSource)))
        {
            return false;
        }

        var currentOffset = GetAnimatedVerticalOffset(scrollViewer);
        if (double.IsNaN(currentOffset) || double.IsInfinity(currentOffset))
            currentOffset = scrollViewer.VerticalOffset;

        var targetOffset = GetTargetVerticalOffset(scrollViewer);
        if (double.IsNaN(targetOffset) || double.IsInfinity(targetOffset))
            targetOffset = currentOffset;

        var wheelStep = GetScrollAmount(optionsSource) * Math.Max(1d, Math.Abs(e.Delta) / 120d);
        var delta = e.Delta > 0 ? -wheelStep : wheelStep;
        var nextOffset = Math.Clamp(targetOffset + delta, 0d, scrollViewer.ScrollableHeight);

        if (Math.Abs(nextOffset - targetOffset) < 0.1d && Math.Abs(nextOffset - currentOffset) < 0.1d)
        {
            e.Handled = true;
            return true;
        }

        SetTargetVerticalOffset(scrollViewer, nextOffset);
        SetAnimatedVerticalOffset(scrollViewer, currentOffset);
        SetIsAnimating(scrollViewer, true);
        var animationVersion = GetAnimationVersion(scrollViewer) + 1;
        SetAnimationVersion(scrollViewer, animationVersion);

        var durationMilliseconds = GetWheelAnimationDurationMilliseconds(optionsSource);
        if (GetAllowContentScroll(optionsSource))
            durationMilliseconds = Math.Clamp(durationMilliseconds, 100d, 160d);

        var animation = new DoubleAnimation
        {
            From = currentOffset,
            To = nextOffset,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (GetAnimationVersion(scrollViewer) != animationVersion)
                return;

            SetIsInternalScrollUpdate(scrollViewer, true);
            scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetAnimatedVerticalOffset(scrollViewer, nextOffset);
            scrollViewer.ScrollToVerticalOffset(nextOffset);
            SetTargetVerticalOffset(scrollViewer, nextOffset);
            SetIsInternalScrollUpdate(scrollViewer, false);
            SetIsAnimating(scrollViewer, false);
        };

        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        e.Handled = true;
        return true;
    }

    public static bool HandleMouseWheelFromDescendant(
        DependencyObject root,
        MouseWheelEventArgs e,
        bool handleWhenUnavailable = false)
    {
        if (FindDescendant<ScrollViewer>(root) is not { } scrollViewer)
        {
            if (handleWhenUnavailable)
                e.Handled = true;
            return false;
        }

        var handled = HandleMouseWheel(scrollViewer, e, root);
        if (!handled && handleWhenUnavailable)
            e.Handled = true;
        return handled;
    }

    private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer || GetIsInternalScrollUpdate(scrollViewer))
            return;

        var offset = (double)e.NewValue;
        if (double.IsNaN(offset) || double.IsInfinity(offset))
            return;

        SetIsInternalScrollUpdate(scrollViewer, true);
        scrollViewer.ScrollToVerticalOffset(offset);
        SetIsInternalScrollUpdate(scrollViewer, false);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
