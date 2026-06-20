using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Behaviors;

namespace Launcher.App.Controls;

public partial class ListPageItemButton : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ListPageItemButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ListPageItemButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TrailingTextProperty =
        DependencyProperty.Register(nameof(TrailingText), typeof(string), typeof(ListPageItemButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TrailingContentProperty =
        DependencyProperty.Register(nameof(TrailingContent), typeof(object), typeof(ListPageItemButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IconSourceProperty =
        DependencyProperty.Register(nameof(IconSource), typeof(ImageSource), typeof(ListPageItemButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(ListPageItemButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ListPageItemButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ListPageItemButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ListPageItemButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFirstVisibleProperty =
        DependencyProperty.Register(nameof(IsFirstVisible), typeof(bool), typeof(ListPageItemButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsLastVisibleProperty =
        DependencyProperty.Register(nameof(IsLastVisible), typeof(bool), typeof(ListPageItemButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsPreviousItemHighlightedProperty =
        DependencyProperty.Register(nameof(IsPreviousItemHighlighted), typeof(bool), typeof(ListPageItemButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsPointerOverOptionProperty =
        DependencyProperty.Register(nameof(IsPointerOverOption), typeof(bool), typeof(ListPageItemButton), new PropertyMetadata(false));

    public static readonly DependencyProperty ShouldPlayEnterAnimationProperty =
        DependencyProperty.Register(
            nameof(ShouldPlayEnterAnimation),
            typeof(bool),
            typeof(ListPageItemButton),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShouldPlayEnterAnimationChanged));

    public static readonly DependencyProperty EnterAnimationIndexProperty =
        DependencyProperty.Register(nameof(EnterAnimationIndex), typeof(int), typeof(ListPageItemButton), new PropertyMetadata(0));

    public static readonly DependencyProperty ItemMarginProperty =
        DependencyProperty.Register(nameof(ItemMargin), typeof(Thickness), typeof(ListPageItemButton), new PropertyMetadata(new Thickness(0, 0, 12, 0)));

    public static readonly DependencyProperty IconColumnWidthProperty =
        DependencyProperty.Register(nameof(IconColumnWidth), typeof(GridLength), typeof(ListPageItemButton), new PropertyMetadata(new GridLength(50)));

    public static readonly DependencyProperty IconWidthProperty =
        DependencyProperty.Register(nameof(IconWidth), typeof(double), typeof(ListPageItemButton), new PropertyMetadata(32d));

    public static readonly DependencyProperty IconHeightProperty =
        DependencyProperty.Register(nameof(IconHeight), typeof(double), typeof(ListPageItemButton), new PropertyMetadata(32d));

    public static readonly DependencyProperty IconMarginProperty =
        DependencyProperty.Register(nameof(IconMargin), typeof(Thickness), typeof(ListPageItemButton), new PropertyMetadata(new Thickness(10, 0, 0, 0)));

    public static readonly DependencyProperty IconScalingModeProperty =
        DependencyProperty.Register(nameof(IconScalingMode), typeof(BitmapScalingMode), typeof(ListPageItemButton), new PropertyMetadata(BitmapScalingMode.NearestNeighbor));

    public static readonly DependencyProperty TextMarginProperty =
        DependencyProperty.Register(nameof(TextMargin), typeof(Thickness), typeof(ListPageItemButton), new PropertyMetadata(new Thickness(5, 0, 0, 0)));

    public static readonly DependencyProperty TrailingMarginProperty =
        DependencyProperty.Register(nameof(TrailingMargin), typeof(Thickness), typeof(ListPageItemButton), new PropertyMetadata(new Thickness(12, 0, 24, 0)));

    public static readonly DependencyProperty TitleFontSizeProperty =
        DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(ListPageItemButton), new PropertyMetadata(16d));

    public static readonly DependencyProperty TitleFontWeightProperty =
        DependencyProperty.Register(nameof(TitleFontWeight), typeof(FontWeight), typeof(ListPageItemButton), new PropertyMetadata(FontWeights.SemiBold));

    public static readonly DependencyProperty SubtitleFontSizeProperty =
        DependencyProperty.Register(nameof(SubtitleFontSize), typeof(double), typeof(ListPageItemButton), new PropertyMetadata(12d));

    public static readonly DependencyProperty SubtitleForegroundProperty =
        DependencyProperty.Register(nameof(SubtitleForeground), typeof(Brush), typeof(ListPageItemButton), new PropertyMetadata(null));

    public static readonly DependencyProperty TrailingFontSizeProperty =
        DependencyProperty.Register(nameof(TrailingFontSize), typeof(double), typeof(ListPageItemButton), new PropertyMetadata(13d));

    public static readonly DependencyProperty TrailingForegroundProperty =
        DependencyProperty.Register(nameof(TrailingForeground), typeof(Brush), typeof(ListPageItemButton), new PropertyMetadata(null));

    public ListPageItemButton()
    {
        InitializeComponent();
        SetResourceReference(SubtitleForegroundProperty, "Brush.Text.Muted");
        SetResourceReference(TrailingForegroundProperty, "Brush.Text.Subtle");
        Loaded += (_, _) => PlayEnterAnimationIfNeeded();
    }

    public Button InnerButton => PART_Button;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string TrailingText
    {
        get => (string)GetValue(TrailingTextProperty);
        set => SetValue(TrailingTextProperty, value);
    }

    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsFirstVisible
    {
        get => (bool)GetValue(IsFirstVisibleProperty);
        set => SetValue(IsFirstVisibleProperty, value);
    }

    public bool IsLastVisible
    {
        get => (bool)GetValue(IsLastVisibleProperty);
        set => SetValue(IsLastVisibleProperty, value);
    }

    public bool IsPreviousItemHighlighted
    {
        get => (bool)GetValue(IsPreviousItemHighlightedProperty);
        set => SetValue(IsPreviousItemHighlightedProperty, value);
    }

    public bool IsPointerOverOption
    {
        get => (bool)GetValue(IsPointerOverOptionProperty);
        set => SetValue(IsPointerOverOptionProperty, value);
    }

    public bool ShouldPlayEnterAnimation
    {
        get => (bool)GetValue(ShouldPlayEnterAnimationProperty);
        set => SetValue(ShouldPlayEnterAnimationProperty, value);
    }

    public int EnterAnimationIndex
    {
        get => (int)GetValue(EnterAnimationIndexProperty);
        set => SetValue(EnterAnimationIndexProperty, value);
    }

    public Thickness ItemMargin
    {
        get => (Thickness)GetValue(ItemMarginProperty);
        set => SetValue(ItemMarginProperty, value);
    }

    public GridLength IconColumnWidth
    {
        get => (GridLength)GetValue(IconColumnWidthProperty);
        set => SetValue(IconColumnWidthProperty, value);
    }

    public double IconWidth
    {
        get => (double)GetValue(IconWidthProperty);
        set => SetValue(IconWidthProperty, value);
    }

    public double IconHeight
    {
        get => (double)GetValue(IconHeightProperty);
        set => SetValue(IconHeightProperty, value);
    }

    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public BitmapScalingMode IconScalingMode
    {
        get => (BitmapScalingMode)GetValue(IconScalingModeProperty);
        set => SetValue(IconScalingModeProperty, value);
    }

    public Thickness TextMargin
    {
        get => (Thickness)GetValue(TextMarginProperty);
        set => SetValue(TextMarginProperty, value);
    }

    public Thickness TrailingMargin
    {
        get => (Thickness)GetValue(TrailingMarginProperty);
        set => SetValue(TrailingMarginProperty, value);
    }

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public FontWeight TitleFontWeight
    {
        get => (FontWeight)GetValue(TitleFontWeightProperty);
        set => SetValue(TitleFontWeightProperty, value);
    }

    public double SubtitleFontSize
    {
        get => (double)GetValue(SubtitleFontSizeProperty);
        set => SetValue(SubtitleFontSizeProperty, value);
    }

    public Brush SubtitleForeground
    {
        get => (Brush)GetValue(SubtitleForegroundProperty);
        set => SetValue(SubtitleForegroundProperty, value);
    }

    public double TrailingFontSize
    {
        get => (double)GetValue(TrailingFontSizeProperty);
        set => SetValue(TrailingFontSizeProperty, value);
    }

    public Brush TrailingForeground
    {
        get => (Brush)GetValue(TrailingForegroundProperty);
        set => SetValue(TrailingForegroundProperty, value);
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        IsPointerOverOption = true;
        OptionHoverBehavior.SetIsExternalActive(PART_Button, true);

        if (TrailingContent is null)
            return;

        AnimateTrailingVisibility(0, 1, TimeSpan.FromMilliseconds(140));
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        IsPointerOverOption = false;
        OptionHoverBehavior.SetIsExternalActive(PART_Button, false);

        if (TrailingContent is null)
            return;

        AnimateTrailingVisibility(1, 0, TimeSpan.FromMilliseconds(180));
    }

    private void PlayEnterAnimationIfNeeded()
    {
        if (!ShouldPlayEnterAnimation)
        {
            ResetVisual();
            return;
        }

        ShouldPlayEnterAnimation = false;

        var delay = TimeSpan.FromMilliseconds(Math.Min(EnterAnimationIndex, 12) * 30);
        var duration = TimeSpan.FromMilliseconds(330);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleTransform = new ScaleTransform(0.96, 0.96);
        var translateTransform = new TranslateTransform(0, 14);
        AnimatedRoot.RenderTransform = new TransformGroup
        {
            Children =
            {
                scaleTransform,
                translateTransform
            }
        };

        AnimatedRoot.Opacity = 0;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => BeginEnterAnimation(
                scaleTransform,
                translateTransform,
                delay,
                duration,
                easing));
    }

    private static void OnShouldPlayEnterAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListPageItemButton { IsLoaded: true } button && e.NewValue is true)
            button.PlayEnterAnimationIfNeeded();
    }

    private void ResetVisual()
    {
        AnimatedRoot.BeginAnimation(OpacityProperty, null);
        AnimatedRoot.Opacity = 1;
        AnimatedRoot.RenderTransform = null;
        TrailingContentPresenter.BeginAnimation(OpacityProperty, null);
    }

    private void BeginEnterAnimation(
        ScaleTransform scaleTransform,
        TranslateTransform translateTransform,
        TimeSpan delay,
        TimeSpan duration,
        IEasingFunction easing)
    {
        if (!AnimatedRoot.IsLoaded)
            return;

        AnimatedRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration)
            {
                BeginTime = delay,
                EasingFunction = easing
            });

        scaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, duration)
            {
                BeginTime = delay,
                EasingFunction = easing
            });

        scaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, duration)
            {
                BeginTime = delay,
                EasingFunction = easing
            });

        translateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, duration)
            {
                BeginTime = delay,
                EasingFunction = easing
            });
    }

    private void AnimateTrailingVisibility(double trailingTextOpacity, double trailingContentOpacity, TimeSpan duration)
    {
        TrailingTextBlock.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(trailingTextOpacity, duration));
        TrailingContentPresenter.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(trailingContentOpacity, duration));
    }
}
