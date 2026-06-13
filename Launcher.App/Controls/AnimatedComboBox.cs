using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public class AnimatedComboBox : ComboBox
{
    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(210);
    private static readonly Duration CloseDuration = TimeSpan.FromMilliseconds(180);
    private static readonly IEasingFunction OpenEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction CloseEasing = new CubicEase { EasingMode = EasingMode.EaseInOut };

    public static readonly DependencyProperty IsPopupOpenProperty =
        DependencyProperty.Register(
            nameof(IsPopupOpen),
            typeof(bool),
            typeof(AnimatedComboBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDropDownClosingProperty =
        DependencyProperty.Register(
            nameof(IsDropDownClosing),
            typeof(bool),
            typeof(AnimatedComboBox),
            new PropertyMetadata(false));

    private readonly DependencyPropertyDescriptor dropDownDescriptor;
    private DispatcherTimer? closeTimer;
    private FrameworkElement? popupSurface;
    private ScaleTransform? scaleTransform;
    private TranslateTransform? translateTransform;

    public AnimatedComboBox()
    {
        dropDownDescriptor = DependencyPropertyDescriptor.FromProperty(IsDropDownOpenProperty, typeof(ComboBox));
        dropDownDescriptor.AddValueChanged(this, OnDropDownOpenChanged);

        Unloaded += (_, _) =>
        {
            closeTimer?.Stop();
            dropDownDescriptor.RemoveValueChanged(this, OnDropDownOpenChanged);
        };
    }

    public bool IsPopupOpen
    {
        get => (bool)GetValue(IsPopupOpenProperty);
        set => SetValue(IsPopupOpenProperty, value);
    }

    public bool IsDropDownClosing
    {
        get => (bool)GetValue(IsDropDownClosingProperty);
        set => SetValue(IsDropDownClosingProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        popupSurface = GetTemplateChild("PopupSurface") as FrameworkElement;
        if (popupSurface is not null)
            popupSurface.CacheMode = new BitmapCache();
        EnsurePopupTransforms();
        if (IsPopupOpen && popupSurface is not null)
            SetPopupVisualState(1, 0, 1);
    }

    private void OnDropDownOpenChanged(object? sender, EventArgs e)
    {
        if (IsDropDownOpen)
        {
            BeginOpenAnimation();
            return;
        }

        BeginCloseAnimation();
    }

    private void BeginOpenAnimation()
    {
        closeTimer?.Stop();
        IsDropDownClosing = false;
        IsPopupOpen = true;

        Dispatcher.BeginInvoke(() =>
        {
            if (popupSurface is null)
                return;

            EnsurePopupTransforms();
            popupSurface.IsHitTestVisible = true;
            popupSurface.BeginAnimation(OpacityProperty, null);
            scaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translateTransform?.BeginAnimation(TranslateTransform.YProperty, null);

            SetPopupVisualState(0, -10, 0.92);

            popupSurface.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = OpenEasing });
            scaleTransform?.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.92, 1, OpenDuration) { EasingFunction = OpenEasing });
            translateTransform?.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(-10, 0, OpenDuration) { EasingFunction = OpenEasing });
        }, DispatcherPriority.Loaded);
    }

    private void BeginCloseAnimation()
    {
        if (!IsPopupOpen)
            return;

        closeTimer?.Stop();
        IsDropDownClosing = true;

        if (popupSurface is not null)
        {
            EnsurePopupTransforms();
            popupSurface.IsHitTestVisible = false;
            popupSurface.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(popupSurface.Opacity, 0, CloseDuration) { EasingFunction = CloseEasing });
            scaleTransform?.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(scaleTransform?.ScaleY ?? 1, 0.92, CloseDuration) { EasingFunction = CloseEasing });
            translateTransform?.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(translateTransform?.Y ?? 0, -8, CloseDuration) { EasingFunction = CloseEasing });
        }

        closeTimer = new DispatcherTimer { Interval = CloseDuration.TimeSpan };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer?.Stop();
            closeTimer = null;
            IsPopupOpen = false;
            IsDropDownClosing = false;
            if (popupSurface is not null)
                SetPopupVisualState(0, -8, 0.92);
        };
        closeTimer.Start();
    }

    private void EnsurePopupTransforms()
    {
        if (popupSurface is null)
            return;

        popupSurface.RenderTransformOrigin = new Point(0.5, 0);
        if (popupSurface.RenderTransform is TransformGroup { Children.Count: 2 } group
            && group.Children[0] is ScaleTransform existingScale
            && group.Children[1] is TranslateTransform existingTranslate)
        {
            scaleTransform = existingScale;
            translateTransform = existingTranslate;
            return;
        }

        scaleTransform = new ScaleTransform(1, 1);
        translateTransform = new TranslateTransform(0, 0);
        popupSurface.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection
            {
                scaleTransform,
                translateTransform
            }
        };
    }

    private void SetPopupVisualState(double opacity, double translateY, double scaleY)
    {
        if (popupSurface is null)
            return;

        popupSurface.Opacity = opacity;
        if (scaleTransform is not null)
            scaleTransform.ScaleY = scaleY;
        if (translateTransform is not null)
            translateTransform.Y = translateY;
    }
}
