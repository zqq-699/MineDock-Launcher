using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Behaviors;

namespace Launcher.App.Controls;

public partial class SecondaryMenuOptionButton : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SecondaryMenuOptionButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextMarginProperty =
        DependencyProperty.Register(nameof(TextMargin), typeof(Thickness), typeof(SecondaryMenuOptionButton), new PropertyMetadata(new Thickness(4, 0, 8, 0)));

    public static readonly DependencyProperty IconModeProperty =
        DependencyProperty.Register(nameof(IconMode), typeof(string), typeof(SecondaryMenuOptionButton), new PropertyMetadata("Svg"));

    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(SecondaryMenuOptionButton), new PropertyMetadata(null));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SecondaryMenuOptionButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AvatarSourceProperty =
        DependencyProperty.Register(nameof(AvatarSource), typeof(object), typeof(SecondaryMenuOptionButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SecondaryMenuOptionButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SecondaryMenuOptionButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SecondaryMenuOptionButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsExternalMouseOverProperty =
        DependencyProperty.Register(
            nameof(IsExternalMouseOver),
            typeof(bool),
            typeof(SecondaryMenuOptionButton),
            new PropertyMetadata(false, OnPointerStateChanged));

    public static readonly DependencyProperty IsPointerOverOptionProperty =
        DependencyProperty.Register(nameof(IsPointerOverOption), typeof(bool), typeof(SecondaryMenuOptionButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IconWidthProperty =
        DependencyProperty.Register(nameof(IconWidth), typeof(double), typeof(SecondaryMenuOptionButton), new PropertyMetadata(22d));

    public static readonly DependencyProperty IconHeightProperty =
        DependencyProperty.Register(nameof(IconHeight), typeof(double), typeof(SecondaryMenuOptionButton), new PropertyMetadata(22d));

    public static readonly DependencyProperty GlyphFontFamilyProperty =
        DependencyProperty.Register(nameof(GlyphFontFamily), typeof(FontFamily), typeof(SecondaryMenuOptionButton), new PropertyMetadata(new FontFamily("Microsoft YaHei UI")));

    public static readonly DependencyProperty GlyphFontSizeProperty =
        DependencyProperty.Register(nameof(GlyphFontSize), typeof(double), typeof(SecondaryMenuOptionButton), new PropertyMetadata(18d));

    public static readonly DependencyProperty GlyphFontWeightProperty =
        DependencyProperty.Register(nameof(GlyphFontWeight), typeof(FontWeight), typeof(SecondaryMenuOptionButton), new PropertyMetadata(FontWeights.Medium));

    public static readonly RoutedEvent RefreshRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(RefreshRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(SecondaryMenuOptionButton));

    public SecondaryMenuOptionButton()
    {
        InitializeComponent();
        MouseEnter += (_, _) => UpdatePointerOverOption();
        MouseLeave += (_, _) => UpdatePointerOverOption();
    }

    public event RoutedEventHandler RefreshRequested
    {
        add => AddHandler(RefreshRequestedEvent, value);
        remove => RemoveHandler(RefreshRequestedEvent, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Thickness TextMargin
    {
        get => (Thickness)GetValue(TextMarginProperty);
        set => SetValue(TextMarginProperty, value);
    }

    public string IconMode
    {
        get => (string)GetValue(IconModeProperty);
        set => SetValue(IconModeProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public object? AvatarSource
    {
        get => GetValue(AvatarSourceProperty);
        set => SetValue(AvatarSourceProperty, value);
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

    public bool IsExternalMouseOver
    {
        get => (bool)GetValue(IsExternalMouseOverProperty);
        set => SetValue(IsExternalMouseOverProperty, value);
    }

    public bool IsPointerOverOption
    {
        get => (bool)GetValue(IsPointerOverOptionProperty);
        set => SetValue(IsPointerOverOptionProperty, value);
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

    public FontFamily GlyphFontFamily
    {
        get => (FontFamily)GetValue(GlyphFontFamilyProperty);
        set => SetValue(GlyphFontFamilyProperty, value);
    }

    public double GlyphFontSize
    {
        get => (double)GetValue(GlyphFontSizeProperty);
        set => SetValue(GlyphFontSizeProperty, value);
    }

    public FontWeight GlyphFontWeight
    {
        get => (FontWeight)GetValue(GlyphFontWeightProperty);
        set => SetValue(GlyphFontWeightProperty, value);
    }

    private void Button_OnClick(object sender, RoutedEventArgs e)
    {
        if (!IsSelected)
            return;

        Dispatcher.BeginInvoke(
            () => RaiseEvent(new RoutedEventArgs(RefreshRequestedEvent, this)),
            DispatcherPriority.Background);
    }

    private static void OnPointerStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is SecondaryMenuOptionButton button)
            button.UpdatePointerOverOption();
    }

    private void UpdatePointerOverOption()
    {
        var isActive = IsMouseOver || IsExternalMouseOver;
        IsPointerOverOption = isActive;
        OptionHoverBehavior.SetIsExternalActive(PART_Button, isActive);
    }
}
