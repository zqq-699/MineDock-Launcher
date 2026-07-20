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

using System.Windows.Media;

namespace Launcher.App.Services;

public interface IThemeService
{
    EffectiveTheme EffectiveTheme { get; }

    bool BackgroundBlurDisabled { get; }

    bool ImageBackgroundStylesEnabled { get; }

    event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    event EventHandler<BackgroundBlurDisabledChangedEventArgs>? BackgroundBlurDisabledChanged;

    void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent,
        bool disableBackgroundBlur);

    void ApplyAccent(string? accentColor);

    void ApplyBackgroundOpacity(int opacityPercent);

    void ApplyBackgroundBlurDisabled(bool disabled);

    void ApplyImageBackgroundStyles(bool enabled);

    object? GetResource(object key);

    Brush? GetBrush(object key);

    Color? GetColor(object key);
}
