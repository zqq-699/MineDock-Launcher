using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Launcher.App.Views.Home;

public partial class HomePageView : UserControl
{
    private const double FallbackPanelWidth = 224;
    private const double FallbackPinnedContentGap = 24;
    private const double FallbackAnimationDurationMilliseconds = 320;
    private const double FallbackAnimationEasePower = 2.4;
    private static readonly Thickness FallbackPanelMargin = new(24, 24, 0, 24);

    public static readonly DependencyProperty IsLaunchMenuPinnedProperty =
        DependencyProperty.Register(
            nameof(IsLaunchMenuPinned),
            typeof(bool),
            typeof(HomePageView),
            new PropertyMetadata(false, OnIsLaunchMenuPinnedChanged));

    private bool hasAppliedInitialContentAlignment;
    private int contentAnimationGeneration;

    public HomePageView()
    {
        InitializeComponent();

        SetBinding(IsLaunchMenuPinnedProperty, new Binding("IsLaunchMenuPinned"));
        Loaded += OnLoaded;
    }

    public bool IsLaunchMenuPinned
    {
        get => (bool)GetValue(IsLaunchMenuPinnedProperty);
        set => SetValue(IsLaunchMenuPinnedProperty, value);
    }

    public FrameworkElement RootElement => PageRoot;

    internal FrameworkElement ContentHostElement => HomeLaunchContentHost;

    internal TranslateTransform ContentTranslateTransform => HomeLaunchContentTranslate;

    internal double PinnedContentOffsetX => CalculatePinnedContentOffsetX();

    private static void OnIsLaunchMenuPinnedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HomePageView view)
            view.ApplyContentAlignment(animate: view.IsLoaded && view.hasAppliedInitialContentAlignment);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyContentAlignment(animate: false);
        hasAppliedInitialContentAlignment = true;
    }

    private void ApplyContentAlignment(bool animate)
    {
        var generation = ++contentAnimationGeneration;
        var targetX = IsLaunchMenuPinned ? CalculatePinnedContentOffsetX() : 0;
        AnimateDouble(HomeLaunchContentTranslate, TranslateTransform.XProperty, targetX, animate, generation);
    }

    private double CalculatePinnedContentOffsetX()
    {
        var panelMargin = GetPanelMargin();
        var panelWidth = GetResourceDouble("HomeLaunchMenuPanelWidth", FallbackPanelWidth);
        var pinnedContentGap = GetResourceDouble("HomeLaunchPinnedContentGap", FallbackPinnedContentGap);
        return (panelMargin.Left + panelWidth + pinnedContentGap) / 2;
    }

    private void AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double to,
        bool animate,
        int generation)
    {
        if (target is not IAnimatable animatable)
        {
            target.SetValue(property, to);
            return;
        }

        var from = GetCurrentDouble(target, property);
        animatable.BeginAnimation(property, null);
        target.SetValue(property, from);

        if (!animate || Math.Abs(from - to) < 0.1)
        {
            target.SetValue(property, to);
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = GetAnimationDuration(),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = CreateAnimationEasing()
        };
        animation.Completed += (_, _) =>
        {
            if (generation != contentAnimationGeneration)
                return;

            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
        };

        animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double GetCurrentDouble(DependencyObject target, DependencyProperty property)
    {
        var value = (double)target.GetValue(property);
        return double.IsNaN(value) ? 0 : value;
    }

    private Duration GetAnimationDuration()
    {
        return new Duration(TimeSpan.FromMilliseconds(GetResourceDouble(
            "HomeLaunchMenuAnimationDurationMilliseconds",
            FallbackAnimationDurationMilliseconds)));
    }

    private IEasingFunction CreateAnimationEasing()
    {
        return new PowerEase
        {
            Power = GetResourceDouble("HomeLaunchMenuAnimationEasePower", FallbackAnimationEasePower),
            EasingMode = EasingMode.EaseOut
        };
    }

    private Thickness GetPanelMargin()
    {
        return TryFindResource("HomeLaunchMenuPanelMargin") is Thickness margin
            ? margin
            : FallbackPanelMargin;
    }

    private double GetResourceDouble(string key, double fallback)
    {
        return TryFindResource(key) is double value ? value : fallback;
    }
}
