/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;

namespace Launcher.App.Behaviors;

public static class SurfaceShadow
{
    public static readonly DependencyProperty SuppressChildShadowsProperty =
        DependencyProperty.RegisterAttached(
            "SuppressChildShadows",
            typeof(bool),
            typeof(SurfaceShadow),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetSuppressChildShadows(DependencyObject element) =>
        (bool)element.GetValue(SuppressChildShadowsProperty);

    public static void SetSuppressChildShadows(DependencyObject element, bool value) =>
        element.SetValue(SuppressChildShadowsProperty, value);
}
