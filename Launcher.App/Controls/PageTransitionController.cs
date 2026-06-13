using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public sealed class PageTransitionController
{
    private const double TransitionOffset = 22;

    private static readonly Duration TransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly string[] PageOrder =
    [
        "Account",
        "Home",
        "Download",
        "GameSettings",
        "Resources",
        "Settings"
    ];

    private readonly Dispatcher dispatcher;
    private readonly Func<string, FrameworkElement?> resolvePageRoot;
    private string? currentPage;
    private int transitionToken;

    public PageTransitionController(
        Dispatcher dispatcher,
        Func<string, FrameworkElement?> resolvePageRoot,
        string? initialPage)
    {
        this.dispatcher = dispatcher;
        this.resolvePageRoot = resolvePageRoot;
        currentPage = initialPage;
    }

    public void MoveTo(string newPage)
    {
        if (string.Equals(currentPage, newPage, StringComparison.OrdinalIgnoreCase))
            return;

        var oldPage = currentPage;
        currentPage = newPage;
        var startOffset = GetTransitionStartOffset(oldPage, newPage);

        var target = resolvePageRoot(newPage);
        if (target is null)
            return;

        var token = ++transitionToken;
        PreparePageForTransition(target, startOffset);
        dispatcher.BeginInvoke(
            () => AnimatePage(newPage, target, startOffset, token),
            DispatcherPriority.Render);
    }

    private static double GetTransitionStartOffset(string? oldPage, string newPage)
    {
        if (string.IsNullOrWhiteSpace(oldPage))
            return TransitionOffset;

        var oldIndex = Array.IndexOf(PageOrder, oldPage);
        var newIndex = Array.IndexOf(PageOrder, newPage);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            return TransitionOffset;

        return newIndex > oldIndex ? TransitionOffset : -TransitionOffset;
    }

    private static TranslateTransform EnsureTranslateTransform(FrameworkElement target)
    {
        if (target.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        target.RenderTransform = transform;
        return transform;
    }

    private static void PreparePageForTransition(FrameworkElement target, double startOffset)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 0;

        var transform = EnsureTranslateTransform(target);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = startOffset;
    }

    private void AnimatePage(string page, FrameworkElement target, double startOffset, int token)
    {
        if (token != transitionToken || !string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase))
            return;

        var transform = EnsureTranslateTransform(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        target.Opacity = 0;
        transform.Y = startOffset;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TransitionDuration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        fadeAnimation.Completed += (_, _) =>
        {
            if (token == transitionToken && string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase))
                target.Opacity = 1;
        };

        var slideAnimation = new DoubleAnimation
        {
            From = startOffset,
            To = 0,
            Duration = TransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        slideAnimation.Completed += (_, _) =>
        {
            if (token == transitionToken && string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase))
                transform.Y = 0;
        };

        target.BeginAnimation(UIElement.OpacityProperty, fadeAnimation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, slideAnimation, HandoffBehavior.SnapshotAndReplace);
    }
}
