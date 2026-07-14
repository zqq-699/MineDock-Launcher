/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Allows standard loaders to keep version metadata private while publishing
/// verified shared game content straight into the real Minecraft directory.
/// </summary>
internal interface ISeparatedInstallPathLoaderProvider
{
    Task<string> InstallWithSeparatedPathsAsync(
        string minecraftVersion,
        MinecraftInstallPathLayout installPathLayout,
        string isolatedVersionName,
        string? loaderVersion,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond);
}
