using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Launcher.App.Services;

public sealed class SlidingContentTransitionCoordinator
{
    private static readonly TimeSpan StepTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan FloatingElementFadeDuration = TimeSpan.FromMilliseconds(180);

    private readonly FrameworkElement loadedElement;
    private readonly FrameworkElement contentHost;
    private readonly FrameworkElement primaryLayer;
    private readonly FrameworkElement secondaryLayer;
    private readonly IReadOnlyList<FrameworkElement> secondaryFloatingElements;
    private readonly bool useSlideTransition;
    private bool isSecondaryLayerVisible;
    private int transitionToken;

    public SlidingContentTransitionCoordinator(
        FrameworkElement loadedElement,
        FrameworkElement contentHost,
        FrameworkElement primaryLayer,
        FrameworkElement secondaryLayer,
        IEnumerable<FrameworkElement>? secondaryFloatingElements = null,
        bool useSlideTransition = true)
    {
        this.loadedElement = loadedElement;
        this.contentHost = contentHost;
        this.primaryLayer = primaryLayer;
        this.secondaryLayer = secondaryLayer;
        this.secondaryFloatingElements = secondaryFloatingElements?.ToArray() ?? [];
        this.useSlideTransition = useSlideTransition;
    }

    public void Sync(bool showSecondaryLayer)
    {
        transitionToken++;
        isSecondaryLayerVisible = showSecondaryLayer;

        ResetLayer(primaryLayer, isVisible: !showSecondaryLayer);
        ResetLayer(secondaryLayer, isVisible: showSecondaryLayer);
        SyncFloatingElements(showSecondaryLayer);
    }

    public void AnimateTo(bool showSecondaryLayer)
    {
        if (isSecondaryLayerVisible == showSecondaryLayer)
        {
            Sync(showSecondaryLayer);
            return;
        }

        if (!loadedElement.IsLoaded || (useSlideTransition && contentHost.ActualWidth <= 0))
        {
            Sync(showSecondaryLayer);
            return;
        }

        var previousLayer = isSecondaryLayerVisible ? secondaryLayer : primaryLayer;
        var nextLayer = showSecondaryLayer ? secondaryLayer : primaryLayer;
        var direction = showSecondaryLayer ? 1 : -1;
        var width = Math.Max(contentHost.ActualWidth, 1);
        var token = ++transitionToken;
        isSecondaryLayerVisible = showSecondaryLayer;

        var previousTransform = EnsureTranslateTransform(previousLayer);
        var nextTransform = EnsureTranslateTransform(nextLayer);

        previousLayer.Visibility = Visibility.Visible;
        previousLayer.Opacity = 1;
        previousTransform.BeginAnimation(TranslateTransform.XProperty, null);
        previousTransform.X = 0;

        nextLayer.Visibility = Visibility.Visible;
        nextLayer.Opacity = 0;
        nextTransform.BeginAnimation(TranslateTransform.XProperty, null);
        nextTransform.X = useSlideTransition ? width * direction : 0;

        AnimateFloatingElements(showSecondaryLayer, token);

        var previousSlide = CreateSlideAnimation(0, useSlideTransition ? -width * direction : 0);
        var nextSlide = CreateSlideAnimation(useSlideTransition ? width * direction : 0, 0);
        var previousFade = CreateSlideAnimation(1, 0);
        var nextFade = CreateSlideAnimation(0, 1);

        var completionAnimation = useSlideTransition ? nextSlide : nextFade;
        completionAnimation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            ResetLayer(previousLayer, isVisible: false);
            ResetLayer(nextLayer, isVisible: true);
        };

        previousLayer.BeginAnimation(UIElement.OpacityProperty, previousFade, HandoffBehavior.SnapshotAndReplace);
        previousTransform.BeginAnimation(TranslateTransform.XProperty, previousSlide, HandoffBehavior.SnapshotAndReplace);
        nextLayer.BeginAnimation(UIElement.OpacityProperty, nextFade, HandoffBehavior.SnapshotAndReplace);
        nextTransform.BeginAnimation(TranslateTransform.XProperty, nextSlide, HandoffBehavior.SnapshotAndReplace);
    }

    private void SyncFloatingElements(bool showSecondaryLayer)
    {
        foreach (var element in secondaryFloatingElements)
            ResetFloatingElement(element, showSecondaryLayer);
    }

    private static void ResetLayer(FrameworkElement layer, bool isVisible)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        var transform = EnsureTranslateTransform(layer);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        layer.Opacity = isVisible ? 1 : 0;
        layer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ResetFloatingElement(FrameworkElement element, bool isVisible)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = isVisible ? 1 : 0;
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.IsHitTestVisible = isVisible;
    }

    private void AnimateFloatingElements(bool showSecondaryLayer, int token)
    {
        foreach (var element in secondaryFloatingElements)
        {
            if (showSecondaryLayer)
                FadeFloatingElementIn(element, token);
            else
                FadeFloatingElementOut(element, token);
        }
    }

    private void FadeFloatingElementIn(FrameworkElement element, int token)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;

        var animation = CreateFloatingElementFadeAnimation(element.Opacity, 1);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = true;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void FadeFloatingElementOut(FrameworkElement element, int token)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.IsHitTestVisible = false;

        var animation = CreateFloatingElementFadeAnimation(element.Opacity, 0);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0;
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = false;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateFloatingElementFadeAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, FloatingElementFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private static DoubleAnimation CreateSlideAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, StepTransitionDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private static TranslateTransform EnsureTranslateTransform(FrameworkElement layer)
    {
        if (layer.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        layer.RenderTransform = transform;
        return transform;
    }
}
