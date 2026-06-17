using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Launcher.App.Controls;

public class AnimatedComboBox : ComboBox
{
    private const double PopupGap = 10;
    private const double DefaultDropDownItemHeightEstimate = 38;
    private const double PopupVerticalPaddingEstimate = 10;
    private static readonly TimeSpan PopupRealtimeBlurInterval = TimeSpan.FromMilliseconds(33);
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
    private TextBlock? selectionTextBlock;
    private ContentPresenter? selectionContentPresenter;
    private ScaleTransform? scaleTransform;
    private TranslateTransform? translateTransform;
    private bool opensAbove;

    public AnimatedComboBox()
    {
        dropDownDescriptor = DependencyPropertyDescriptor.FromProperty(IsDropDownOpenProperty, typeof(ComboBox));
        dropDownDescriptor.AddValueChanged(this, OnDropDownOpenChanged);

        Unloaded += (_, _) =>
        {
            closeTimer?.Stop();
            DetachPopupListBox();
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
        DetachPopup();
        base.OnApplyTemplate();
        popup = GetTemplateChild("PART_Popup") as Popup;
        popupListBox = GetTemplateChild("PART_DropDownList") as ListBox;
        popupSurface = GetTemplateChild("PopupSurface") as FrameworkElement;
        selectionTextBlock = GetTemplateChild("SelectionTextBlock") as TextBlock;
        selectionContentPresenter = GetTemplateChild("SelectionContentPresenter") as ContentPresenter;
        AttachPopup();
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
            if (popupSurface is BackdropBlurBorder blurBorder)
                blurBorder.StartRealtimeRefresh(PopupRealtimeBlurInterval);
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
                StopRealtimeBackdropRefresh();
                RequestBackdropRefreshAfterLayout();
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
            if (popupSurface is BackdropBlurBorder blurBorder)
                blurBorder.StopRealtimeRefresh();
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
                StopRealtimeBackdropRefresh();
                SetPopupVisualState(0, GetCloseTranslateOffset(), 0.92);
            }
        };
        closeTimer.Start();
    }

    private void EnsurePopupTransforms()
    {
        if (popupSurface is null)
            return;

        UpdateBackdropSampleOffset();
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

        var popupHeight = GetPopupHeightEstimate();
        var topLeft = PointToScreen(new Point(0, 0));
        var controlTop = topLeft.Y;
        var controlBottom = topLeft.Y + ActualHeight;
        var belowSpace = SystemParameters.WorkArea.Bottom - controlBottom;
        var aboveSpace = controlTop - SystemParameters.WorkArea.Top;

        opensAbove = belowSpace < popupHeight + PopupGap && aboveSpace > belowSpace;

        popup.Placement = opensAbove ? PlacementMode.Top : PlacementMode.Bottom;
        popup.VerticalOffset = opensAbove ? -PopupGap : PopupGap;
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
    }

    private void DetachPopupListBox()
    {
        if (popupListBox is null)
            return;

        popupListBox.PreviewMouseLeftButtonUp -= PopupListBox_PreviewMouseLeftButtonUp;
        popupListBox.PreviewKeyDown -= PopupListBox_PreviewKeyDown;
        popupListBox = null;
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
        popup = null;
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

    private void Popup_Opened(object? sender, EventArgs e)
    {
        RequestBackdropRefreshAfterLayout();
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        StopRealtimeBackdropRefresh();
    }

    private void RequestBackdropRefreshAfterLayout()
    {
        if (popupSurface is not BackdropBlurBorder blurBorder)
            return;

        Dispatcher.BeginInvoke(
            () =>
            {
                blurBorder.RequestRefresh();
                Dispatcher.BeginInvoke(blurBorder.RequestRefresh, DispatcherPriority.Loaded);
            },
            DispatcherPriority.Render);
    }

    private void StopRealtimeBackdropRefresh()
    {
        if (popupSurface is BackdropBlurBorder blurBorder)
            blurBorder.StopRealtimeRefresh();
    }

    private void UpdateBackdropSampleOffset()
    {
        if (popupSurface is not BackdropBlurBorder blurBorder)
            return;

        blurBorder.UseSourceElementAsSampleOrigin = true;
        blurBorder.SampleOffsetX = 0;
        var popupVerticalOffset = popup?.VerticalOffset ?? (opensAbove ? -PopupGap : PopupGap);
        blurBorder.SampleOffsetY = opensAbove
            ? -popupSurface.ActualHeight + popupVerticalOffset
            : ActualHeight + popupVerticalOffset;
    }

    private void UpdateSelectionPresenterMode()
    {
        if (selectionTextBlock is null || selectionContentPresenter is null)
            return;

        var useSelectionTemplate = SelectionItemTemplate is not null || SelectionItemTemplateSelector is not null;
        selectionTextBlock.Visibility = useSelectionTemplate ? Visibility.Collapsed : Visibility.Visible;
        selectionContentPresenter.Visibility = useSelectionTemplate ? Visibility.Visible : Visibility.Collapsed;
    }
}
