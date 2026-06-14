using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Launcher.App.Behaviors;

public static class ProgressBarAnimation
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty DurationMillisecondsProperty =
        DependencyProperty.RegisterAttached(
            "DurationMilliseconds",
            typeof(double),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(360d));

    public static readonly DependencyProperty AnimatedWidthProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedWidth",
            typeof(double),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(0d));

    private static readonly DependencyProperty AnimationVersionProperty =
        DependencyProperty.RegisterAttached(
            "AnimationVersion",
            typeof(int),
            typeof(ProgressBarAnimation),
            new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetDurationMilliseconds(DependencyObject element) => (double)element.GetValue(DurationMillisecondsProperty);

    public static void SetDurationMilliseconds(DependencyObject element, double value) => element.SetValue(DurationMillisecondsProperty, value);

    public static double GetAnimatedWidth(DependencyObject element) => (double)element.GetValue(AnimatedWidthProperty);

    public static void SetAnimatedWidth(DependencyObject element, double value) => element.SetValue(AnimatedWidthProperty, value);

    private static int GetAnimationVersion(DependencyObject element) => (int)element.GetValue(AnimationVersionProperty);

    private static void SetAnimationVersion(DependencyObject element, int value) => element.SetValue(AnimationVersionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar progressBar)
            return;

        if ((bool)e.NewValue)
        {
            progressBar.Loaded += ProgressBar_Loaded;
            progressBar.Unloaded += ProgressBar_Unloaded;
            progressBar.SizeChanged += ProgressBar_SizeChanged;
            progressBar.ValueChanged += ProgressBar_ValueChanged;
            UpdateAnimatedWidth(progressBar, animate: false);
            return;
        }

        progressBar.Loaded -= ProgressBar_Loaded;
        progressBar.Unloaded -= ProgressBar_Unloaded;
        progressBar.SizeChanged -= ProgressBar_SizeChanged;
        progressBar.ValueChanged -= ProgressBar_ValueChanged;
        progressBar.BeginAnimation(AnimatedWidthProperty, null);
    }

    private static void ProgressBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProgressBar progressBar)
            UpdateAnimatedWidth(progressBar, animate: false);
    }

    private static void ProgressBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProgressBar progressBar)
            progressBar.BeginAnimation(AnimatedWidthProperty, null);
    }

    private static void ProgressBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ProgressBar progressBar)
            UpdateAnimatedWidth(progressBar, animate: false);
    }

    private static void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is ProgressBar progressBar)
            UpdateAnimatedWidth(progressBar, animate: true);
    }

    private static void UpdateAnimatedWidth(ProgressBar progressBar, bool animate)
    {
        var targetWidth = CalculateTargetWidth(progressBar);
        var currentWidth = GetAnimatedWidth(progressBar);

        if (!animate
            || !progressBar.IsLoaded
            || targetWidth <= currentWidth
            || Math.Abs(targetWidth - currentWidth) < 0.5)
        {
            progressBar.BeginAnimation(AnimatedWidthProperty, null);
            SetAnimatedWidth(progressBar, targetWidth);
            return;
        }

        var animationVersion = GetAnimationVersion(progressBar) + 1;
        SetAnimationVersion(progressBar, animationVersion);

        var durationMilliseconds = Math.Clamp(GetDurationMilliseconds(progressBar), 80d, 900d);
        var animation = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (GetAnimationVersion(progressBar) != animationVersion)
                return;

            progressBar.BeginAnimation(AnimatedWidthProperty, null);
            SetAnimatedWidth(progressBar, targetWidth);
        };

        progressBar.BeginAnimation(AnimatedWidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double CalculateTargetWidth(ProgressBar progressBar)
    {
        if (progressBar.ActualWidth <= 0
            || double.IsNaN(progressBar.ActualWidth)
            || double.IsInfinity(progressBar.ActualWidth))
        {
            return 0d;
        }

        var range = progressBar.Maximum - progressBar.Minimum;
        if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range))
            return 0d;

        var ratio = (progressBar.Value - progressBar.Minimum) / range;
        ratio = Math.Clamp(ratio, 0d, 1d);
        return progressBar.ActualWidth * ratio;
    }
}
