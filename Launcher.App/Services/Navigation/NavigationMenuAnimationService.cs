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
using System.Windows.Media.Animation;
using Launcher.App.Animations;

namespace Launcher.App.Services;

public sealed class NavigationMenuAnimationService
{
    private const double CollapsedWidth = 62;
    private const double ExpandedWidth = 176;

    private static readonly TimeSpan WidthAnimationDuration = TimeSpan.FromMilliseconds(360);

    private readonly ColumnDefinition menuColumn;

    public NavigationMenuAnimationService(ColumnDefinition menuColumn)
    {
        this.menuColumn = menuColumn;
    }

    public void SetExpanded(bool isExpanded)
    {
        menuColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        menuColumn.Width = GetWidth(isExpanded);
    }

    public void AnimateExpanded(bool isExpanded)
    {
        var targetWidth = GetWidth(isExpanded);
        var animation = new GridLengthAnimation
        {
            From = new GridLength(menuColumn.ActualWidth),
            To = targetWidth,
            Duration = WidthAnimationDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        animation.Completed += (_, _) =>
        {
            menuColumn.Width = targetWidth;
        };

        menuColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private static GridLength GetWidth(bool isExpanded)
    {
        return new GridLength(isExpanded ? ExpandedWidth : CollapsedWidth);
    }
}
