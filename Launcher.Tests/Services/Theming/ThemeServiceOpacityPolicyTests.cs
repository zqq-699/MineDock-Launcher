/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services.Theming;

public sealed class ThemeServiceOpacityPolicyTests
{
    [Theory]
    [InlineData(0, true, 100)]
    [InlineData(42, true, 100)]
    [InlineData(100, true, 100)]
    [InlineData(0, false, 0)]
    [InlineData(42, false, 42)]
    [InlineData(120, false, 100)]
    public void ResolveEffectiveBackgroundOpacityPercent_OnlyUsesPreferenceForAcrylic(
        int preferredOpacityPercent,
        bool backgroundBlurDisabled,
        int expected)
    {
        Assert.Equal(
            expected,
            ThemeService.ResolveEffectiveBackgroundOpacityPercent(
                preferredOpacityPercent,
                backgroundBlurDisabled));
    }

    [Theory]
    [InlineData(false, EffectiveTheme.Dark, false)]
    [InlineData(false, EffectiveTheme.Light, false)]
    [InlineData(true, EffectiveTheme.Light, false)]
    [InlineData(true, EffectiveTheme.Dark, true)]
    public void ResolveSurfaceBackdropBlurEnabled_RequiresDarkImageMode(
        bool imageBackgroundStylesEnabled,
        EffectiveTheme effectiveTheme,
        bool expected)
    {
        Assert.Equal(
            expected,
            ThemeService.ResolveSurfaceBackdropBlurEnabled(
                imageBackgroundStylesEnabled,
                effectiveTheme));
    }
}
