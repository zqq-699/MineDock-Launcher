/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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

/// <summary>
/// 在原生 ComboBox 弹出行为之上增加方向感知动画、滚轮转发和选择展示切换。
/// </summary>
public class AnimatedComboBox : ComboBox
{
    // 打开略慢、关闭略快；动画只修改透明度和变换，避免在 Popup 独立窗口中触发布局抖动。
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
        // 主题切换会重新应用模板，查找新部件前必须解除旧 Popup 和列表事件。
        DetachPopupListBox();
        DetachPopupSurface();
        DetachPopup();
        base.OnApplyTemplate();
        popup = GetTemplateChild("PART_Popup") as Popup;
        popupListBox = GetTemplateChild("PART_DropDownList") as ListBox;
        popupSurface = GetTemplateChild("PopupSurface") as FrameworkElement;
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
        // IsDropDownOpen 是状态源；打开和关闭分别维护自己的计时器与命中状态。
        if (IsDropDownOpen)
        {
            BeginOpenAnimation();
            return;
        }

        BeginCloseAnimation();
    }

    private void BeginOpenAnimation()
    {
        // 先计算 Popup 实际位于控件上方还是下方，再从相应方向进入。
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

        // Popup 的视觉树要到下一次调度才具有可靠尺寸，延迟一拍后再启动动画。
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

            popupSurface.BeginAnimation(OpacityProperty, opacityAnimation);
            scaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            translateTransform?.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }, DispatcherPriority.Input);
    }

    private void BeginCloseAnimation()
    {
        // 关闭期间暂时保留 Popup 表面用于播放退场，计时结束后再回到完全关闭状态。
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
        // 复用模板表面的 TransformGroup，不覆盖主题可能已经设置的其他变换。
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
        // WPF 会按屏幕空间自动翻转 Popup，因此根据屏幕坐标判断真实展开方向。
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
        // 首次展开前 ActualHeight 为零，使用条目高度和 MaxDropDownHeight 给动画原点一个稳定估值。
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
        // Popup 内容可能延迟生成，附加方法保持幂等，允许在模板应用和 Opened 时重复调用。
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
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        StopPopupScrollAnimation();
        DetachPopupWheelOwner();
    }

    private void PopupListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 在预览阶段开始关闭，避免默认选择行为先销毁 Popup 导致退场动画丢失。
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
        // Popup 是独立窗口，滚轮事件不会自然路由到宿主控件，需要显式交给内部列表滚动。
        if (!IsPopupOpen || popupListBox is null)
            return;

        ScrollPopupList(e);
    }

    private void Owner_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 光标位于 Popup 时阻止背景页面随滚轮移动，保持下拉列表是唯一滚动目标。
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

    private void UpdateSelectionPresenterMode()
    {
        // 打开列表时隐藏模板中的重复选择展示，关闭后再恢复紧凑的单项展示。
        if (selectionTextBlock is null || selectionContentPresenter is null)
            return;

        var useSelectionTemplate = SelectionItemTemplate is not null || SelectionItemTemplateSelector is not null;
        selectionTextBlock.Visibility = useSelectionTemplate ? Visibility.Collapsed : Visibility.Visible;
        selectionContentPresenter.Visibility = useSelectionTemplate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StopPopupScrollAnimation()
    {
        // Popup 关闭或模板替换时停止旧 ScrollViewer 动画，避免动画时钟继续持有视觉对象。
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
