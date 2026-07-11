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
using System.Windows.Markup;

namespace Launcher.App.Controls;

[ContentProperty(nameof(ListContent))]
public sealed class DeferredListContentHost : ContentControl
{
    public static readonly DependencyProperty ListContentProperty =
        DependencyProperty.Register(
            nameof(ListContent),
            typeof(object),
            typeof(DeferredListContentHost),
            new PropertyMetadata(null, OnPresentationChanged));

    public static readonly DependencyProperty IsListVisibleProperty =
        DependencyProperty.Register(
            nameof(IsListVisible),
            typeof(bool),
            typeof(DeferredListContentHost),
            new PropertyMetadata(false, OnPresentationChanged));

    public DeferredListContentHost()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Focusable = false;
        IsTabStop = false;
        UpdatePresentation();
    }

    public object? ListContent
    {
        get => GetValue(ListContentProperty);
        set => SetValue(ListContentProperty, value);
    }

    public bool IsListVisible
    {
        get => (bool)GetValue(IsListVisibleProperty);
        set => SetValue(IsListVisibleProperty, value);
    }

    private static void OnPresentationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DeferredListContentHost host)
            host.UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        Content = IsListVisible ? ListContent : null;
        Visibility = IsListVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
