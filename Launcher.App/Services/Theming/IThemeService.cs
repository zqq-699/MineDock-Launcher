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

namespace Launcher.App.Services;

public interface IThemeService
{
    EffectiveTheme EffectiveTheme { get; }

    string BackgroundEffect { get; }

    event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    event EventHandler<BackgroundEffectChangedEventArgs>? BackgroundEffectChanged;

    void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent);

    void ApplyAccent(string? accentColor);

    void ApplyBackgroundOpacity(int opacityPercent);

    void ApplyBackgroundEffect(string? backgroundEffect, bool enableImageControlBlur);
}
