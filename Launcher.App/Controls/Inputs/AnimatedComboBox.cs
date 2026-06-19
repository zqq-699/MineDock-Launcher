using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Behaviors;

namespace Launcher.App.Controls;

public class AnimatedComboBox : ComboBox
{
    private const double PopupGap = 6;
    private const double PopupShadowPadding = 14;
    private const double DefaultDropDownItemHeightEstimate = 38;
    private const double PopupVerticalPaddingEstimate = 10;
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

    public static readonly DependencyProperty DropDownItemContainerStyleProperty =
        DependencyProperty.Register(
            nameof(DropDownItemContainerStyle),
            typeof(Style),
            typeof(AnimatedComboBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectionItemTemplateProperty =
        DependencyProperty.Register(
            nameof(SelectionItemTemplate),
            typeof(DataTemplate),
            typeof(AnimatedComboBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectionItemTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(SelectionItemTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(AnimatedComboBox),
            new PropertyMetadata(null));

    private readonly DependencyPropertyDescriptor dropDownDescriptor;
    private DispatcherTimer? closeTimer;
    private Popup? popup;
    private ListBox? popupListBox;
    private FrameworkElement? popupSurface;
    private BackdropBlurBorder? popupBlurSurface;
    private TextBlock? selectionTextBlock;
    private ContentPresenter? selectionContentPresenter;
    private ScaleTransform? scaleTransform;
    private TranslateTransform? translateTransform;
    private Window? popupWheelOwner;
    private bool opensAbove;

    public AnimatedComboBox()
    {
        dropDownDescriptor = DependencyPropertyDescriptor.FromProperty(IsDropDownOpenProperty, typeof(ComboBox));
        dropDownDescriptor.AddValueChanged(this, OnDropDownOpenChanged);

        Unloaded += (_, _) =>
        {
            closeTimer?.Stop();
            DetachPopupListBox();
            DetachPopupSurface();
            DetachPopupWheelOwner();
            DetachPopup();
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

    public Style? DropDownItemContainerStyle
    {
        get => (Style?)GetValue(DropDownItemContainerStyleProperty);
        set => SetValue(DropDownItemContainerStyleProperty, value);
    }

    public DataTemplate? SelectionItemTemplate
    {
        get => (DataTemplate?)GetValue(SelectionItemTemplateProperty);
        set => SetValue(SelectionItemTemplateProperty, value);
    }

    public DataTemplateSelector? SelectionItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(SelectionItemTemplateSelectorProperty);
        set => SetValue(SelectionItemTemplateSelectorProperty, value);
    }

    public override void OnApplyTemplate()
    {
        DetachPopupListBox();
        DetachPopupSurface();
        DetachPopup();
        base.OnApplyTemplate();
        popup = GetTemplateChild("PART_Popup") as Popup;
        popupListBox = GetTemplateChild("PART_DropDownList") as ListBox;
        popupSurface = GetTemplateChild("PopupSurface") as FrameworkElement;
        popupBlurSurface = GetTemplateChild("PopupBlurSurface") as BackdropBlurBorder;
        selectionTextBlock = GetTemplateChild("SelectionTextBlock") as TextBlock;
        selectionContentPresenter = GetTemplateChild("SelectionContentPresenter") as ContentPresenter;
        AttachPopup();
        AttachPopupSurface();
        AttachPopupListBox();
        if (popupSurface is not null)
        {
            popupSurface.CacheMode = new BitmapCache();
            popupSurface.IsHitTestVisible = false;
        }
        UpdateSelectionPresenterMode();
        EnsurePopupTransforms();
        if (IsPopupOpen && popupSurface is not null)
            SetPopupVisualState(1, 0, 1);
        else
            SetPopupVisualState(0, -10, 0.92);
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
        EnsurePopupTransforms();
        UpdatePopupPlacement();
        if (popupSurface is not null)
        {
            popupSurface.BeginAnimation(OpacityProperty, null);
            scaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translateTransform?.BeginAnimation(TranslateTransform.YProperty, null);
            popupSurface.IsHitTestVisible = false;
            SetPopupVisualState(0, GetOpenTranslateOffset(), 0.92);
        }

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

            SetPopupVisualState(0, GetOpenTranslateOffset(), 0.92);

            var opacityAnimation = new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = OpenEasing };
            var scaleAnimation = new DoubleAnimation(0.92, 1, OpenDuration) { EasingFunction = OpenEasing };
            var translateAnimation = new DoubleAnimation(GetOpenTranslateOffset(), 0, OpenDuration) { EasingFunction = OpenEasing };
            translateAnimation.Completed += (_, _) =>
            {
                Dispatcher.BeginInvoke(RefreshPopupBlur, DispatcherPriority.Render);
            };

            popupSurface.BeginAnimation(OpacityProperty, opacityAnimation);
            scaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            translateTransform?.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }, DispatcherPriority.Input);
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
                new DoubleAnimation(translateTransform?.Y ?? 0, GetCloseTranslateOffset(), CloseDuration) { EasingFunction = CloseEasing });
        }

        closeTimer = new DispatcherTimer { Interval = CloseDuration.TimeSpan };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer?.Stop();
            closeTimer = null;
            IsPopupOpen = false;
            IsDropDownClosing = false;
            if (popupSurface is not null)
            {
                SetPopupVisualState(0, GetCloseTranslateOffset(), 0.92);
            }
        };
        closeTimer.Start();
    }

    private void EnsurePopupTransforms()
    {
        if (popupSurface is null)
            return;

        popupSurface.RenderTransformOrigin = opensAbove ? new Point(0.5, 1) : new Point(0.5, 0);
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

    private void UpdatePopupPlacement()
    {
        if (popup is null)
            return;

        var popupHeight = GetPopupHeightEstimate() + PopupShadowPadding * 2;
        var topLeft = PointToScreen(new Point(0, 0));
        var controlTop = topLeft.Y;
        var controlBottom = topLeft.Y + ActualHeight;
        var belowSpace = SystemParameters.WorkArea.Bottom - controlBottom;
        var aboveSpace = controlTop - SystemParameters.WorkArea.Top;

        opensAbove = belowSpace < popupHeight + PopupGap && aboveSpace > belowSpace;

        popup.Placement = opensAbove ? PlacementMode.Top : PlacementMode.Bottom;
        popup.HorizontalOffset = 0;
        popup.VerticalOffset = opensAbove
            ? PopupShadowPadding - PopupGap
            : PopupGap - PopupShadowPadding;
        EnsurePopupTransforms();
    }

    private double GetPopupHeightEstimate()
    {
        var maxHeight = double.IsNaN(MaxDropDownHeight) || MaxDropDownHeight <= 0 ? 260 : MaxDropDownHeight;
        if (Items.Count <= 0)
            return maxHeight;

        var desiredHeight = Items.Count * GetDropDownItemHeightEstimate() + PopupVerticalPaddingEstimate;
        return Math.Min(desiredHeight, maxHeight);
    }

    private double GetOpenTranslateOffset() => opensAbove ? 10 : -10;

    private double GetCloseTranslateOffset() => opensAbove ? 8 : -8;

    private double GetDropDownItemHeightEstimate()
    {
        if (ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement container
            && container.ActualHeight > 0)
        {
            return container.ActualHeight;
        }

        return Math.Max(DefaultDropDownItemHeightEstimate, FontSize + 22);
    }

    private void AttachPopupListBox()
    {
        if (popupListBox is null)
            return;

        popupListBox.PreviewMouseLeftButtonUp += PopupListBox_PreviewMouseLeftButtonUp;
        popupListBox.PreviewKeyDown += PopupListBox_PreviewKeyDown;
        popupListBox.PreviewMouseWheel += PopupDropDown_PreviewMouseWheel;
    }

    private void AttachPopupSurface()
    {
        if (popupSurface is null)
            return;

        popupSurface.PreviewMouseWheel += PopupDropDown_PreviewMouseWheel;
    }

    private void DetachPopupListBox()
    {
        if (popupListBox is null)
            return;

        popupListBox.PreviewMouseLeftButtonUp -= PopupListBox_PreviewMouseLeftButtonUp;
        popupListBox.PreviewKeyDown -= PopupListBox_PreviewKeyDown;
        popupListBox.PreviewMouseWheel -= PopupDropDown_PreviewMouseWheel;
        popupListBox = null;
    }

    private void DetachPopupSurface()
    {
        if (popupSurface is null)
            return;

        popupSurface.PreviewMouseWheel -= PopupDropDown_PreviewMouseWheel;
        popupSurface = null;
    }

    private void AttachPopup()
    {
        if (popup is null)
            return;

        popup.Opened += Popup_Opened;
        popup.Closed += Popup_Closed;
    }

    private void DetachPopup()
    {
        if (popup is null)
            return;

        popup.Opened -= Popup_Opened;
        popup.Closed -= Popup_Closed;
        DetachPopupWheelOwner();
        popup = null;
    }

    private void Popup_Opened(object? sender, EventArgs e)
    {
        AttachPopupWheelOwner();
        popupListBox?.Focus();
        RefreshPopupBlur();
        Dispatcher.BeginInvoke(RefreshPopupBlur, DispatcherPriority.Render);
        Dispatcher.BeginInvoke(RefreshPopupBlur, DispatcherPriority.ApplicationIdle);
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        StopPopupScrollAnimation();
        DetachPopupWheelOwner();
    }

    private void PopupListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsDropDownOpen || sender is not ListBox listBox || e.OriginalSource is not DependencyObject source)
            return;

        if (ItemsControl.ContainerFromElement(listBox, source) is ListBoxItem { IsEnabled: true })
            IsDropDownOpen = false;
    }

    private void PopupListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsDropDownOpen)
            return;

        if (e.Key is Key.Enter or Key.Space or Key.Escape)
        {
            IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void PopupDropDown_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsPopupOpen || popupListBox is null)
            return;

        ScrollPopupList(e);
    }

    private void Owner_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsPopupOpen)
            return;

        if (IsCursorOverPopupSurface())
        {
            ScrollPopupList(e);
            return;
        }

        e.Handled = true;
    }

    private void ScrollPopupList(MouseWheelEventArgs e)
    {
        if (popupListBox is not { } listBox)
            return;

        listBox.ApplyTemplate();
        listBox.UpdateLayout();
        SmoothScrollBehavior.HandleMouseWheelFromDescendant(listBox, e, handleWhenUnavailable: true);
    }

    private void AttachPopupWheelOwner()
    {
        var owner = Window.GetWindow(this);
        if (ReferenceEquals(popupWheelOwner, owner))
            return;

        DetachPopupWheelOwner();
        popupWheelOwner = owner;
        popupWheelOwner?.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Owner_PreviewMouseWheel), true);
    }

    private void DetachPopupWheelOwner()
    {
        if (popupWheelOwner is null)
            return;

        popupWheelOwner.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Owner_PreviewMouseWheel));
        popupWheelOwner = null;
    }

    private bool IsCursorOverPopupSurface()
    {
        if (popupSurface is null || !GetCursorPos(out var cursor))
            return false;

        var point = popupSurface.PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X >= 0
            && point.Y >= 0
            && point.X <= popupSurface.ActualWidth
            && point.Y <= popupSurface.ActualHeight;
    }

    private void RefreshPopupBlur()
    {
        if (popupBlurSurface is null)
            return;

        var sourceRoot = Window.GetWindow(this)?.Content as FrameworkElement;
        if (sourceRoot is null)
            return;

        var popupHeight = popupBlurSurface.ActualHeight > 0
            ? popupBlurSurface.ActualHeight
            : popupSurface?.ActualHeight ?? GetPopupHeightEstimate();
        var controlTopLeft = PointToScreen(new Point(0, 0));
        var sampleTop = opensAbove
            ? controlTopLeft.Y - PopupGap - popupHeight
            : controlTopLeft.Y + ActualHeight + PopupGap;
        var sampleOrigin = sourceRoot.PointFromScreen(new Point(controlTopLeft.X, sampleTop));

        popupBlurSurface.SourceElement = sourceRoot;
        popupBlurSurface.UseSourceElementAsRenderRoot = true;
        popupBlurSurface.UseSourceElementAsSampleOrigin = true;
        popupBlurSurface.SampleOffsetX = sampleOrigin.X;
        popupBlurSurface.SampleOffsetY = sampleOrigin.Y;
        popupBlurSurface.RequestRefresh();
    }

    private void UpdateSelectionPresenterMode()
    {
        if (selectionTextBlock is null || selectionContentPresenter is null)
            return;

        var useSelectionTemplate = SelectionItemTemplate is not null || SelectionItemTemplateSelector is not null;
        selectionTextBlock.Visibility = useSelectionTemplate ? Visibility.Collapsed : Visibility.Visible;
        selectionContentPresenter.Visibility = useSelectionTemplate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StopPopupScrollAnimation()
    {
        if (popupListBox is null)
            return;

        popupListBox.ApplyTemplate();
        popupListBox.UpdateLayout();
        SmoothScrollBehavior.CancelAnimationFromDescendant(popupListBox);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }
}
