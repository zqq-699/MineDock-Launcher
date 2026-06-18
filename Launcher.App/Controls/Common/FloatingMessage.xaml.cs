using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Launcher.App.Controls;

public partial class FloatingMessage : UserControl
{
    private static readonly Duration FadeInDuration = TimeSpan.FromMilliseconds(140);
    private static readonly Duration FadeOutDuration = TimeSpan.FromMilliseconds(190);

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(FloatingMessage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(FloatingMessage), new PropertyMetadata(false, OnIsOpenChanged));

    public FloatingMessage()
    {
        InitializeComponent();
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FloatingMessage message)
            message.SetOpenState((bool)e.NewValue);
    }

    private void SetOpenState(bool isOpen)
    {
        BeginAnimation(OpacityProperty, null);
        MessageOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        if (isOpen)
        {
            Visibility = Visibility.Visible;
            AnimateOpacity(Opacity, 1, FadeInDuration);
            AnimateOffset(MessageOffset.Y, 0, FadeInDuration);
            Opacity = 1;
            MessageOffset.Y = 0;
            return;
        }

        if (Visibility != Visibility.Visible)
            return;

        var fadeOut = CreateAnimation(Opacity, 0, FadeOutDuration);
        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            Opacity = 0;
            MessageOffset.Y = -10;
        };

        BeginAnimation(OpacityProperty, fadeOut);
        AnimateOffset(MessageOffset.Y, -10, FadeOutDuration);
    }

    private void AnimateOpacity(double from, double to, Duration duration)
    {
        BeginAnimation(OpacityProperty, CreateAnimation(from, to, duration));
    }

    private void AnimateOffset(double from, double to, Duration duration)
    {
        MessageOffset.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            CreateAnimation(from, to, duration));
    }

    private static DoubleAnimation CreateAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, duration)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }
}
