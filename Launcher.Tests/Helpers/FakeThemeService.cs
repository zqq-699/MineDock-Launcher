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
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Helpers;

internal sealed class FakeThemeService : IThemeService
{
    public EffectiveTheme EffectiveTheme { get; private set; } = EffectiveTheme.Dark;

    public bool BackgroundBlurDisabled => LastDisableBackgroundBlur;

    public string? LastTheme { get; private set; }

    public bool LastFollowSystem { get; private set; }

    public string? LastAccentColor { get; private set; }

    public int LastBackgroundOpacityPercent { get; private set; }

    public bool LastDisableBackgroundBlur { get; private set; }

    public int ApplyCount { get; private set; }

    public int ApplyBackgroundOpacityCount { get; private set; }

    public int ApplyBackgroundBlurDisabledCount { get; private set; }

    public int ApplyAccentCount { get; private set; }

    public event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    public event EventHandler<BackgroundBlurDisabledChangedEventArgs>? BackgroundBlurDisabledChanged;

    public void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent,
        bool disableBackgroundBlur)
    {
        LastTheme = theme;
        LastFollowSystem = followSystem;
        LastBackgroundOpacityPercent = backgroundOpacityPercent;
        var backgroundBlurDisabledChanged = LastDisableBackgroundBlur != disableBackgroundBlur;
        LastDisableBackgroundBlur = disableBackgroundBlur;
        ApplyCount++;
        var oldTheme = EffectiveTheme;
        EffectiveTheme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) && !followSystem
            ? EffectiveTheme.Light
            : EffectiveTheme.Dark;
        if (oldTheme != EffectiveTheme)
            EffectiveThemeChanged?.Invoke(this, new EffectiveThemeChangedEventArgs(oldTheme, EffectiveTheme));
        if (backgroundBlurDisabledChanged)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(disableBackgroundBlur));
    }

    public void ApplyAccent(string? accentColor)
    {
        LastAccentColor = LauncherAccentColors.Normalize(accentColor);
        ApplyAccentCount++;
    }

    public void ApplyBackgroundOpacity(int opacityPercent)
    {
        LastBackgroundOpacityPercent = opacityPercent;
        ApplyBackgroundOpacityCount++;
    }

    public void ApplyBackgroundBlurDisabled(bool disabled)
    {
        var changed = LastDisableBackgroundBlur != disabled;
        LastDisableBackgroundBlur = disabled;
        ApplyBackgroundBlurDisabledCount++;
        if (changed)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(disabled));
    }

    public object? GetResource(object key)
    {
        return null;
    }

    public Brush? GetBrush(object key)
    {
        return null;
    }

    public Color? GetColor(object key)
    {
        return null;
    }
}
