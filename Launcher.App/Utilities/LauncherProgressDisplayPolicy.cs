/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.App.Utilities;

internal static class LauncherProgressDisplayPolicy
{
    /// <summary>Only a body-read event may retain or update the network speed.</summary>
    public static bool IsNetworkTransfer(LauncherProgress progress) =>
        progress.DownloadSpeedText is not null
        || progress.Stage is LaunchProgressStages.DownloadingFiles or LaunchProgressStages.DownloadSpeed;
}
