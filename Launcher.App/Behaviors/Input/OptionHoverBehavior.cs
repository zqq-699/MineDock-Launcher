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

namespace Launcher.App.Behaviors;

public static class OptionHoverBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(OptionHoverBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsExternalActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsExternalActive",
            typeof(bool),
            typeof(OptionHoverBehavior),
            new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(OptionHoverBehavior),
            new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsExternalActive(DependencyObject element) => (bool)element.GetValue(IsExternalActiveProperty);

    public static void SetIsExternalActive(DependencyObject element, bool value) => element.SetValue(IsExternalActiveProperty, value);

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.MouseEnter += Element_OnMouseStateChanged;
            element.MouseLeave += Element_OnMouseStateChanged;
            UpdateIsActive(element);
            return;
        }

        element.MouseEnter -= Element_OnMouseStateChanged;
        element.MouseLeave -= Element_OnMouseStateChanged;
        SetIsActive(element, false);
    }

    private static void OnStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element)
            UpdateIsActive(element);
    }

    private static void Element_OnMouseStateChanged(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
            UpdateIsActive(element);
    }

    private static void UpdateIsActive(FrameworkElement element)
    {
        SetIsActive(element, element.IsMouseOver || GetIsExternalActive(element));
    }
}
