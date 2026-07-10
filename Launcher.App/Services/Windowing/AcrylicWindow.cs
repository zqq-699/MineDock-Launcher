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
using System.Windows.Media;
using System.Windows.Shell;

namespace Launcher.App.Services;

public static class AcrylicWindow
{
    public static void Enable(Window window, IThemeService themeService)
    {
        const NativeBackdrop.DwmSystemBackdropType backdropType = NativeBackdrop.DwmSystemBackdropType.TransientWindow;
        window.SourceInitialized += (_, _) =>
        {
            Apply(window, themeService, backdropType);
        };
        Apply(window, themeService, backdropType);

        EventHandler<EffectiveThemeChangedEventArgs>? themeChangedHandler = null;
        themeChangedHandler = (_, _) =>
        {
            Apply(window, themeService, backdropType);
        };
        EventHandler<BackgroundBlurDisabledChangedEventArgs>? blurDisabledChangedHandler = null;
        blurDisabledChangedHandler = (_, _) =>
        {
            Apply(window, themeService, backdropType);
        };

        themeService.EffectiveThemeChanged += themeChangedHandler;
        themeService.BackgroundBlurDisabledChanged += blurDisabledChangedHandler;
        window.Closed += (_, _) =>
        {
            if (themeChangedHandler is not null)
                themeService.EffectiveThemeChanged -= themeChangedHandler;
            if (blurDisabledChangedHandler is not null)
                themeService.BackgroundBlurDisabledChanged -= blurDisabledChangedHandler;
        };
    }

    private static void Apply(
        Window window,
        IThemeService themeService,
        NativeBackdrop.DwmSystemBackdropType enabledBackdropType)
    {
        var isBackdropEnabled = !themeService.BackgroundBlurDisabled;
        var backdropType = isBackdropEnabled
            ? enabledBackdropType
            : NativeBackdrop.DwmSystemBackdropType.None;

        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => ApplyCore(window, backdropType, themeService.EffectiveTheme, isBackdropEnabled));
            return;
        }

        ApplyCore(window, backdropType, themeService.EffectiveTheme, isBackdropEnabled);
    }

    private static void ApplyCore(
        Window window,
        NativeBackdrop.DwmSystemBackdropType backdropType,
        EffectiveTheme theme,
        bool isBackdropEnabled)
    {
        ApplyWindowChrome(window, isBackdropEnabled);
        ApplyWindowBackground(window, theme, isBackdropEnabled);
        NativeBackdrop.ApplyToWindow(window, backdropType, theme, isBackdropEnabled);
    }

    private static void ApplyWindowChrome(Window window, bool isBackdropEnabled)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is null)
            return;

        chrome.GlassFrameThickness = isBackdropEnabled
            ? new Thickness(-1)
            : new Thickness(0);
    }

    private static void ApplyWindowBackground(Window window, EffectiveTheme theme, bool isBackdropEnabled)
    {
        if (isBackdropEnabled)
        {
            window.Background = Brushes.Transparent;
            return;
        }

        if (global::System.Windows.Application.Current?.TryFindResource("Brush.Surface.Window") is Brush surfaceBrush)
        {
            window.Background = surfaceBrush;
            return;
        }

        window.Background = new SolidColorBrush(NativeBackdrop.GetOpaqueWindowBackgroundColor(theme));
    }
}
