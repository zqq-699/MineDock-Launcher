using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

using Launcher.App.Controls;

namespace Launcher.App.Services;

public sealed class DialogOverlayService
{
    private const double SizeAnimationThreshold = 1;

    private static readonly Duration FadeInDuration = TimeSpan.FromMilliseconds(160);
    private static readonly Duration FadeOutDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration SizeTransitionDuration = TimeSpan.FromMilliseconds(240);
    private readonly Window owner;
    private readonly FrameworkElement sourceLayer;
    private bool isSizeAnimating;

    public DialogOverlayService(Window owner, FrameworkElement sourceLayer)
    {
        this.owner = owner;
        this.sourceLayer = sourceLayer;
    }

    public bool IsSizeAnimating => isSizeAnimating;

    public void QueueRefresh(DialogHost host, int attempts = 5)
    {
        QueueRefresh(host.SurfaceBorder, host.BlurLayerBorder, attempts);
    }

    public void QueueRefresh(FrameworkElement dialog, Border target, int attempts = 5)
    {
        if (target is BackdropBlurBorder blurTarget)
        {
            blurTarget.SourceElement = sourceLayer;
            blurTarget.UseSourceElementAsRenderRoot = true;
            blurTarget.RequestRefresh();
            return;
        }

        target.SetCurrentValue(Border.BackgroundProperty, null);
    }

    public void RefreshNow(DialogHost host)
    {
        RefreshNow(host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void RefreshNow(FrameworkElement dialog, Border target)
    {
        QueueRefresh(dialog, target);
    }

    public void AnimateSizeChange(DialogHost host, double previousHeight)
    {
        AnimateSizeChange(host.SurfaceBorder, host.BlurLayerBorder, previousHeight);
    }

    public void AnimateSizeChange(Border dialog, Border blurTarget, double previousHeight)
    {
        dialog.BeginAnimation(FrameworkElement.HeightProperty, null);
        dialog.Height = double.NaN;
        isSizeAnimating = true;

        owner.UpdateLayout();
        var targetHeight = dialog.ActualHeight;

        if (previousHeight <= 0
            || targetHeight <= 0
            || Math.Abs(previousHeight - targetHeight) <= SizeAnimationThreshold)
        {
            dialog.Height = double.NaN;
            isSizeAnimating = false;
            RefreshNow(dialog, blurTarget);
            return;
        }

        dialog.Height = previousHeight;
        dialog.UpdateLayout();

        var animation = new DoubleAnimation
        {
            From = previousHeight,
            To = targetHeight,
            Duration = SizeTransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            dialog.Height = double.NaN;
            isSizeAnimating = false;
            QueueRefresh(dialog, blurTarget);
        };

        dialog.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    public void Show(DialogHost host)
    {
        Show(host.OverlayRoot, host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void Show(Grid overlay, FrameworkElement dialog, Border blurTarget)
    {
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Visible;
        overlay.Opacity = 0;

        QueueRefresh(dialog, blurTarget);

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = FadeInDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void Hide(DialogHost host, Action? completed = null)
    {
        Hide(host.OverlayRoot, completed);
    }

    public void Hide(Grid overlay, Action? completed = null)
    {
        var currentOpacity = overlay.Opacity;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);

        overlay.Opacity = currentOpacity;
        if (currentOpacity <= 0)
        {
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = 0,
            Duration = FadeOutDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            overlay.Opacity = 0;
            overlay.Visibility = Visibility.Collapsed;
            completed?.Invoke();
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void Prewarm(DialogHost host)
    {
        Prewarm(host.OverlayRoot, host.SurfaceBorder, host.BlurLayerBorder);
    }

    public void Prewarm(Grid overlay, Border dialog, Border blurTarget)
    {
        var originalVisibility = overlay.Visibility;
        var originalOpacity = overlay.Opacity;

        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Hidden;
        overlay.Opacity = 0;

        dialog.ApplyTemplate();
        blurTarget.ApplyTemplate();
        dialog.UpdateLayout();
        QueueRefresh(dialog, blurTarget);

        overlay.Visibility = originalVisibility;
        overlay.Opacity = originalOpacity;
    }
}
