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
using System.Windows.Media.Animation;

namespace Launcher.App.Behaviors.Animation;

public static class AnimatedCollapse
{
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.RegisterAttached(
            "IsExpanded",
            typeof(bool),
            typeof(AnimatedCollapse),
            new PropertyMetadata(true, OnIsExpandedChanged));

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.RegisterAttached(
            "Duration",
            typeof(Duration),
            typeof(AnimatedCollapse),
            new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(180))));

    private static readonly DependencyProperty OriginalHeightProperty =
        DependencyProperty.RegisterAttached(
            "OriginalHeight",
            typeof(double),
            typeof(AnimatedCollapse),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty OriginalMarginProperty =
        DependencyProperty.RegisterAttached(
            "OriginalMargin",
            typeof(Thickness),
            typeof(AnimatedCollapse),
            new PropertyMetadata(new Thickness()));

    private static readonly DependencyProperty HasOriginalHeightProperty =
        DependencyProperty.RegisterAttached(
            "HasOriginalHeight",
            typeof(bool),
            typeof(AnimatedCollapse),
            new PropertyMetadata(false));

    public static bool GetIsExpanded(DependencyObject element)
    {
        return (bool)element.GetValue(IsExpandedProperty);
    }

    public static void SetIsExpanded(DependencyObject element, bool value)
    {
        element.SetValue(IsExpandedProperty, value);
    }

    public static Duration GetDuration(DependencyObject element)
    {
        return (Duration)element.GetValue(DurationProperty);
    }

    public static void SetDuration(DependencyObject element, Duration value)
    {
        element.SetValue(DurationProperty, value);
    }

    private static void OnIsExpandedChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        CaptureOriginalHeight(element);

        var isExpanded = (bool)e.NewValue;
        if (!element.IsLoaded)
        {
            element.Loaded -= Element_Loaded;
            element.Loaded += Element_Loaded;
            ApplyInstantState(element, isExpanded);
            return;
        }

        Animate(element, isExpanded);
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        element.Loaded -= Element_Loaded;
        ApplyInstantState(element, GetIsExpanded(element));
    }

    private static void CaptureOriginalHeight(FrameworkElement element)
    {
        if ((bool)element.GetValue(HasOriginalHeightProperty))
            return;

        element.SetValue(OriginalHeightProperty, element.Height);
        element.SetValue(OriginalMarginProperty, element.Margin);
        element.SetValue(HasOriginalHeightProperty, true);
    }

    private static void ApplyInstantState(FrameworkElement element, bool isExpanded)
    {
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.BeginAnimation(FrameworkElement.MarginProperty, null);

        if (isExpanded)
        {
            RestoreOriginalHeight(element);
            RestoreOriginalMargin(element);
            element.Visibility = Visibility.Visible;
            element.Opacity = 1;
            return;
        }

        element.Height = 0;
        element.Margin = new Thickness();
        element.Opacity = 0;
        element.Visibility = Visibility.Collapsed;
    }

    private static void Animate(FrameworkElement element, bool isExpanded)
    {
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.BeginAnimation(FrameworkElement.MarginProperty, null);

        if (isExpanded)
        {
            AnimateExpand(element);
            return;
        }

        AnimateCollapse(element);
    }

    private static void AnimateExpand(FrameworkElement element)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = 0;
        element.Margin = new Thickness();

        var targetHeight = MeasureExpandedHeight(element);
        element.Height = 0;
        var targetMargin = (Thickness)element.GetValue(OriginalMarginProperty);

        var duration = GetDuration(element);
        var heightAnimation = CreateHeightAnimation(0, targetHeight, duration);
        heightAnimation.Completed += (_, _) =>
        {
            if (!GetIsExpanded(element))
                return;

            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            RestoreOriginalHeight(element);
            RestoreOriginalMargin(element);
            element.Opacity = 1;
        };

        element.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
        element.BeginAnimation(UIElement.OpacityProperty, CreateOpacityAnimation(0, 1, duration));
        element.BeginAnimation(FrameworkElement.MarginProperty, CreateMarginAnimation(new Thickness(), targetMargin, duration));
    }

    private static void AnimateCollapse(FrameworkElement element)
    {
        var startHeight = element.ActualHeight;
        if (startHeight <= 0)
            startHeight = MeasureExpandedHeight(element);

        element.Height = startHeight;
        element.Visibility = Visibility.Visible;
        var startMargin = element.Margin;

        var duration = GetDuration(element);
        var heightAnimation = CreateHeightAnimation(startHeight, 0, duration);
        heightAnimation.Completed += (_, _) =>
        {
            if (GetIsExpanded(element))
                return;

            element.Visibility = Visibility.Collapsed;
            element.Height = 0;
            element.Margin = new Thickness();
            element.Opacity = 0;
        };

        element.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
        element.BeginAnimation(UIElement.OpacityProperty, CreateOpacityAnimation(element.Opacity, 0, duration));
        element.BeginAnimation(FrameworkElement.MarginProperty, CreateMarginAnimation(startMargin, new Thickness(), duration));
    }

    private static double MeasureExpandedHeight(FrameworkElement element)
    {
        var originalHeight = (double)element.GetValue(OriginalHeightProperty);
        if (!double.IsNaN(originalHeight))
            return originalHeight;

        element.Height = double.NaN;
        element.Measure(new Size(element.ActualWidth > 0 ? element.ActualWidth : double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize.Height;
    }

    private static DoubleAnimation CreateHeightAnimation(double from, double to, Duration duration)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = CreateEasingFunction()
        };

        return animation;
    }

    private static DoubleAnimation CreateOpacityAnimation(double from, double to, Duration duration)
    {
        return new DoubleAnimation(from, to, duration)
        {
            EasingFunction = CreateEasingFunction()
        };
    }

    private static ThicknessAnimation CreateMarginAnimation(Thickness from, Thickness to, Duration duration)
    {
        return new ThicknessAnimation(from, to, duration)
        {
            EasingFunction = CreateEasingFunction()
        };
    }

    private static IEasingFunction CreateEasingFunction()
    {
        return new ExponentialEase
        {
            EasingMode = EasingMode.EaseOut,
            Exponent = 6
        };
    }

    private static void RestoreOriginalHeight(FrameworkElement element)
    {
        var originalHeight = (double)element.GetValue(OriginalHeightProperty);
        element.Height = originalHeight;
    }

    private static void RestoreOriginalMargin(FrameworkElement element)
    {
        var originalMargin = (Thickness)element.GetValue(OriginalMarginProperty);
        element.Margin = originalMargin;
    }
}
