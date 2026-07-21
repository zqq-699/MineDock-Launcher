/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services.Theming;

public sealed class LauncherBackgroundPresentationPolicyTests
{
    [Theory]
    [InlineData(LauncherBackgroundEffects.None, 42, true, false, false, false, 100)]
    [InlineData(LauncherBackgroundEffects.Acrylic, 42, true, true, false, false, 42)]
    [InlineData(LauncherBackgroundEffects.Image, 42, true, false, true, true, 100)]
    [InlineData(LauncherBackgroundEffects.Image, 42, false, false, true, false, 100)]
    public void ResolveProducesOneConsistentPresentation(
        string effect,
        int preferredOpacityPercent,
        bool enableImageControlBlur,
        bool expectedWindowBackdrop,
        bool expectedImageBackground,
        bool expectedImageControlBlur,
        int expectedPageOpacity)
    {
        var presentation = LauncherBackgroundPresentationPolicy.Resolve(
            effect,
            preferredOpacityPercent,
            enableImageControlBlur);

        Assert.Equal(LauncherBackgroundEffects.Normalize(effect), presentation.Effect);
        Assert.Equal(expectedWindowBackdrop, presentation.IsWindowBackdropEnabled);
        Assert.Equal(expectedImageBackground, presentation.IsImageBackgroundEnabled);
        Assert.Equal(expectedImageControlBlur, presentation.IsImageControlBlurEnabled);
        Assert.Equal(expectedPageOpacity, presentation.PageBackgroundOpacityPercent);
    }
}
