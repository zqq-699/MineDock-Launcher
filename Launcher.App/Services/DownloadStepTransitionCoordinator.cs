using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.ViewModels;

namespace Launcher.App.Services;

public sealed class DownloadStepTransitionCoordinator
{
    private static readonly TimeSpan StepTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan StepButtonFadeDuration = TimeSpan.FromMilliseconds(180);

    private readonly FrameworkElement loadedElement;
    private readonly FrameworkElement stepHost;
    private readonly FrameworkElement versionListLayer;
    private readonly FrameworkElement instanceOptionsLayer;
    private readonly Button installStepButton;
    private DownloadPageStep displayedStep = DownloadPageStep.VersionList;
    private int transitionToken;

    public DownloadStepTransitionCoordinator(
        FrameworkElement loadedElement,
        FrameworkElement stepHost,
        FrameworkElement versionListLayer,
        FrameworkElement instanceOptionsLayer,
        Button installStepButton)
    {
        this.loadedElement = loadedElement;
        this.stepHost = stepHost;
        this.versionListLayer = versionListLayer;
        this.instanceOptionsLayer = instanceOptionsLayer;
        this.installStepButton = installStepButton;
    }

    public void Sync(DownloadPageStep step)
    {
        transitionToken++;
        displayedStep = step;

        ResetStepLayer(versionListLayer, step is DownloadPageStep.VersionList);
        ResetStepLayer(instanceOptionsLayer, step is DownloadPageStep.InstanceOptions);
        SyncStepButtons(step);
    }

    public void AnimateTo(DownloadPageStep nextStep)
    {
        if (displayedStep == nextStep)
        {
            Sync(nextStep);
            return;
        }

        if (!loadedElement.IsLoaded || stepHost.ActualWidth <= 0)
        {
            Sync(nextStep);
            return;
        }

        var oldStep = displayedStep;
        displayedStep = nextStep;
        var token = ++transitionToken;
        var width = Math.Max(stepHost.ActualWidth, 1);
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

        var oldSlide = CreateStepAnimation(0, -width * direction);
        var newSlide = CreateStepAnimation(width * direction, 0);
        var oldFade = CreateStepAnimation(1, 0);
        var newFade = CreateStepAnimation(0, 1);

        newSlide.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            ResetStepLayer(oldLayer, isVisible: false);
            ResetStepLayer(nextLayer, isVisible: true);
        };

        oldLayer.BeginAnimation(UIElement.OpacityProperty, oldFade, HandoffBehavior.SnapshotAndReplace);
        oldTransform.BeginAnimation(TranslateTransform.XProperty, oldSlide, HandoffBehavior.SnapshotAndReplace);
        nextLayer.BeginAnimation(UIElement.OpacityProperty, newFade, HandoffBehavior.SnapshotAndReplace);
        nextTransform.BeginAnimation(TranslateTransform.XProperty, newSlide, HandoffBehavior.SnapshotAndReplace);
    }

    private FrameworkElement GetStepLayer(DownloadPageStep step)
    {
        return step is DownloadPageStep.InstanceOptions
            ? instanceOptionsLayer
            : versionListLayer;
    }

    private void SyncStepButtons(DownloadPageStep step)
    {
        var isInstanceOptionsStep = step is DownloadPageStep.InstanceOptions;
        ResetStepButton(installStepButton, isInstanceOptionsStep);
    }

    private static void ResetStepLayer(FrameworkElement layer, bool isVisible)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        var transform = EnsureStepTransform(layer);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        layer.Opacity = isVisible ? 1 : 0;
        layer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ResetStepButton(Button button, bool isVisible)
    {
        button.BeginAnimation(UIElement.OpacityProperty, null);
        button.Opacity = isVisible ? 1 : 0;
        button.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        button.IsHitTestVisible = isVisible;
    }

    private void AnimateStepButtons(DownloadPageStep nextStep, int token)
    {
        if (nextStep is DownloadPageStep.InstanceOptions)
        {
            FadeStepButtonIn(installStepButton, token);
            return;
        }

        FadeStepButtonOut(installStepButton, token);
    }

    private void FadeStepButtonIn(Button button, int token)
    {
        button.BeginAnimation(UIElement.OpacityProperty, null);
        button.Visibility = Visibility.Visible;
        button.IsHitTestVisible = true;

        var animation = CreateStepButtonFadeAnimation(button.Opacity, 1);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            button.BeginAnimation(UIElement.OpacityProperty, null);
            button.Opacity = 1;
            button.Visibility = Visibility.Visible;
            button.IsHitTestVisible = true;
        };
        button.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void FadeStepButtonOut(Button button, int token)
    {
        button.BeginAnimation(UIElement.OpacityProperty, null);
        button.IsHitTestVisible = false;

        var animation = CreateStepButtonFadeAnimation(button.Opacity, 0);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            button.BeginAnimation(UIElement.OpacityProperty, null);
            button.Opacity = 0;
            button.Visibility = Visibility.Collapsed;
            button.IsHitTestVisible = false;
        };
        button.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateStepButtonFadeAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, StepButtonFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private static DoubleAnimation CreateStepAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, StepTransitionDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
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
}
