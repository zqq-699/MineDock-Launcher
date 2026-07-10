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

public static class SecondaryMenuButtonBehavior
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(SecondaryMenuButtonBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SuppressSelectedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "SuppressSelectedBackground",
            typeof(bool),
            typeof(SecondaryMenuButtonBehavior),
            new PropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element)
    {
        return (bool)element.GetValue(IsSelectedProperty);
    }

    public static void SetIsSelected(DependencyObject element, bool value)
    {
        element.SetValue(IsSelectedProperty, value);
    }

    public static bool GetSuppressSelectedBackground(DependencyObject element)
    {
        return (bool)element.GetValue(SuppressSelectedBackgroundProperty);
    }

    public static void SetSuppressSelectedBackground(DependencyObject element, bool value)
    {
        element.SetValue(SuppressSelectedBackgroundProperty, value);
    }
}
