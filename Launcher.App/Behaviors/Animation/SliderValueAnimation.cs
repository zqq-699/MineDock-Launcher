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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Launcher.App.Behaviors.Animation;

/// <summary>
/// 在外部目标值变化时平滑驱动 Slider，并在用户拖动期间让交互值保持绝对优先。
/// </summary>
public static class SliderValueAnimation
{
    // 附加属性保存目标、拖动和动画代次，行为状态随 Slider 一起回收。
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SliderValueAnimation),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty TargetValueProperty =
        DependencyProperty.RegisterAttached(
            "TargetValue",
            typeof(double),
            typeof(SliderValueAnimation),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTargetValueChanged));

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.RegisterAttached(
            "Duration",
            typeof(Duration),
            typeof(SliderValueAnimation),
            new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(220))));

    private static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimating",
            typeof(bool),
            typeof(SliderValueAnimation),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsDraggingProperty =
        DependencyProperty.RegisterAttached(
            "IsDragging",
            typeof(bool),
            typeof(SliderValueAnimation),
            new PropertyMetadata(false));

    private static readonly DependencyProperty AnimationVersionProperty =
        DependencyProperty.RegisterAttached(
            "AnimationVersion",
            typeof(int),
            typeof(SliderValueAnimation),
            new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static double GetTargetValue(DependencyObject element)
    {
        return (double)element.GetValue(TargetValueProperty);
    }

    public static void SetTargetValue(DependencyObject element, double value)
    {
        element.SetValue(TargetValueProperty, value);
    }

    public static Duration GetDuration(DependencyObject element)
    {
        return (Duration)element.GetValue(DurationProperty);
    }

    public static void SetDuration(DependencyObject element, Duration value)
    {
        element.SetValue(DurationProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        // 启用时成对注册 Loaded/Unloaded 和拖动事件，重复设置保持幂等。
        if (dependencyObject is not Slider slider)
            return;

        if ((bool)e.NewValue)
        {
            slider.Loaded += Slider_Loaded;
            slider.Unloaded += Slider_Unloaded;
            slider.ValueChanged += Slider_ValueChanged;
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(Slider_DragStarted));
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Slider_DragCompleted));
            return;
        }

        slider.Loaded -= Slider_Loaded;
        slider.Unloaded -= Slider_Unloaded;
        slider.ValueChanged -= Slider_ValueChanged;
        slider.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(Slider_DragStarted));
        slider.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Slider_DragCompleted));
        slider.BeginAnimation(RangeBase.ValueProperty, null);
        SetIsAnimating(slider, false);
    }

    private static void Slider_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        SetSliderValue(slider, GetTargetValue(slider));
    }

    private static void Slider_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            SetIsAnimating(slider, false);
        }
    }

    private static void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        // 用户抓住 Thumb 时立即停止程序动画，避免两套输入同时写 Value。
        if (sender is not Slider slider)
            return;

        SetIsDragging(slider, true);
        slider.BeginAnimation(RangeBase.ValueProperty, null);
        SetIsAnimating(slider, false);
    }

    private static void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // 拖动结束以用户最终值同步 TargetValue，不能反弹到拖动前目标。
        if (sender is not Slider slider)
            return;

        SetIsDragging(slider, false);
        SetTargetValue(slider, SnapToStep(slider, slider.Value));
    }

    private static void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 动画内部 ValueChanged 不回写目标，防止每帧产生 Binding 循环。
        if (sender is not Slider slider
            || !GetIsEnabled(slider)
            || GetIsAnimating(slider))
        {
            return;
        }

        var targetValue = SnapToStep(slider, e.NewValue);

        if (GetIsDragging(slider))
        {
            SetTargetValue(slider, targetValue);
            return;
        }

        SetSliderValue(slider, e.OldValue);
        SetTargetValue(slider, targetValue);
    }

    private static void OnTargetValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        // 拖动期间只记录目标但不动画，正常状态则从当前呈现值平滑接续。
        if (dependencyObject is not Slider slider || !GetIsEnabled(slider))
            return;

        var targetValue = SnapToStep(slider, (double)e.NewValue);
        if (Math.Abs(targetValue - (double)e.NewValue) > 0.01)
        {
            SetTargetValue(slider, targetValue);
            return;
        }

        if (!slider.IsLoaded)
        {
            SetSliderValue(slider, targetValue);
            return;
        }

        AnimateToTarget(slider, targetValue);
    }

    private static void AnimateToTarget(Slider slider, double targetValue)
    {
        // 版本号使旧 Completed 回调失效，连续目标变化不会覆盖最终值。
        var currentValue = CoerceToSliderRange(slider, slider.Value);
        if (Math.Abs(currentValue - targetValue) < 0.01)
        {
            SetSliderValue(slider, targetValue);
            return;
        }

        var animationVersion = GetAnimationVersion(slider) + 1;
        SetAnimationVersion(slider, animationVersion);
        SetIsAnimating(slider, true);

        var animation = new DoubleAnimation
        {
            From = currentValue,
            To = targetValue,
            Duration = GetAnimationDuration(slider),
            EasingFunction = new ExponentialEase
            {
                EasingMode = EasingMode.EaseOut,
                Exponent = 6
            },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (GetAnimationVersion(slider) != animationVersion)
                return;

            slider.BeginAnimation(RangeBase.ValueProperty, null);
            SetIsAnimating(slider, false);
            SetSliderValue(slider, targetValue);
        };

        slider.BeginAnimation(RangeBase.ValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static Duration GetAnimationDuration(Slider slider)
    {
        if (!GetIsDragging(slider))
            return GetDuration(slider);

        var duration = GetDuration(slider);
        if (!duration.HasTimeSpan)
            return duration;

        return new Duration(TimeSpan.FromMilliseconds(Math.Max(80, duration.TimeSpan.TotalMilliseconds * 0.6)));
    }

    private static double CoerceToSliderRange(Slider slider, double value)
    {
        return Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private static double SnapToStep(Slider slider, double value)
    {
        // 最终值按 TickFrequency 对齐，与 Slider 原生步进规则保持一致。
        var clamped = CoerceToSliderRange(slider, value);
        var step = slider.TickFrequency > 0
            ? slider.TickFrequency
            : slider.SmallChange;

        if (step <= 0 || double.IsNaN(step) || double.IsInfinity(step))
            return clamped;

        var snapped = slider.Minimum + Math.Round((clamped - slider.Minimum) / step) * step;
        return CoerceToSliderRange(slider, snapped);
    }

    private static void SetSliderValue(Slider slider, double value)
    {
        SetIsAnimating(slider, true);
        try
        {
            slider.Value = CoerceToSliderRange(slider, value);
        }
        finally
        {
            SetIsAnimating(slider, false);
        }
    }

    private static bool GetIsAnimating(DependencyObject element)
    {
        return (bool)element.GetValue(IsAnimatingProperty);
    }

    private static void SetIsAnimating(DependencyObject element, bool value)
    {
        element.SetValue(IsAnimatingProperty, value);
    }

    private static bool GetIsDragging(DependencyObject element)
    {
        return (bool)element.GetValue(IsDraggingProperty);
    }

    private static void SetIsDragging(DependencyObject element, bool value)
    {
        element.SetValue(IsDraggingProperty, value);
    }

    private static int GetAnimationVersion(DependencyObject element)
    {
        return (int)element.GetValue(AnimationVersionProperty);
    }

    private static void SetAnimationVersion(DependencyObject element, int value)
    {
        element.SetValue(AnimationVersionProperty, value);
    }
}
