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
    private bool isSizeAnimating;

    public DialogOverlayService(Window owner)
    {
        this.owner = owner;
    }

    public bool IsSizeAnimating => isSizeAnimating;

    public void AnimateSizeChange(DialogHost host, double previousHeight)
    {
        AnimateSizeChange(host.SurfaceBorder, previousHeight);
    }

    public void AnimateSizeChange(Border dialog, double previousHeight)
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
        };

        dialog.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    public void Show(DialogHost host)
    {
        Show(host.OverlayRoot);
    }

    public void Show(Grid overlay)
    {
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Visible;
        overlay.Opacity = 0;

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
        Prewarm(host.OverlayRoot, host.SurfaceBorder);
    }

    public void Prewarm(Grid overlay, Border dialog)
    {
        var originalVisibility = overlay.Visibility;
        var originalOpacity = overlay.Opacity;

        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Visibility = Visibility.Hidden;
        overlay.Opacity = 0;

        dialog.ApplyTemplate();
        dialog.UpdateLayout();

        overlay.Visibility = originalVisibility;
        overlay.Opacity = originalOpacity;
    }
}
