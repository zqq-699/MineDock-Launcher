/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.App.Services;

internal readonly record struct LauncherBackgroundPresentation(
    string Effect,
    bool IsWindowBackdropEnabled,
    bool IsImageBackgroundEnabled,
    bool IsImageControlBlurEnabled,
    int PageBackgroundOpacityPercent);

internal static class LauncherBackgroundPresentationPolicy
{
    public static LauncherBackgroundPresentation Resolve(
        string? backgroundEffect,
        int preferredOpacityPercent,
        bool enableImageControlBlur)
    {
        var effect = LauncherBackgroundEffects.Normalize(backgroundEffect);
        var isAcrylic = LauncherBackgroundEffects.IsAcrylic(effect);
        var isImage = LauncherBackgroundEffects.IsImage(effect);
        return new LauncherBackgroundPresentation(
            effect,
            isAcrylic,
            isImage,
            isImage && enableImageControlBlur,
            isAcrylic ? NormalizeOpacity(preferredOpacityPercent) : 100);
    }

    private static int NormalizeOpacity(int opacityPercent) => Math.Clamp(opacityPercent, 0, 100);
}
