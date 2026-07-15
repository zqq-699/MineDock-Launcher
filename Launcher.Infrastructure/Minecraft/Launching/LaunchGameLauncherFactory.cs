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

using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface ILaunchGameLauncher
{
    ValueTask<Process> BuildProcessAsync(string versionName, MLaunchOption launchOption, CancellationToken cancellationToken);
}

internal interface ILaunchGameLauncherFactory
{
    ILaunchGameLauncher Create(
        string minecraftDirectory,
        IProgress<LauncherProgress>? progress,
        int downloadSpeedLimitMbPerSecond = 0);
}

internal sealed class LaunchGameLauncherFactory : ILaunchGameLauncherFactory
{
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;

    public LaunchGameLauncherFactory(IDownloadSpeedLimitState? downloadSpeedLimitState = null)
    {
        this.downloadSpeedLimitState = downloadSpeedLimitState;
    }

    public ILaunchGameLauncher Create(
        string minecraftDirectory,
        IProgress<LauncherProgress>? progress,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var buildProgress = progress is null
            ? null
            : new LaunchProcessBuildProgressAdapter(progress);
        var launcher = VanillaLoaderProvider.CreateLauncher(
            minecraftDirectory,
            buildProgress,
            downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState: downloadSpeedLimitState);
        VanillaLoaderProvider.AttachProgress(launcher, buildProgress);
        return new CmlLaunchGameLauncher(launcher);
    }

    private sealed class CmlLaunchGameLauncher : ILaunchGameLauncher
    {
        private readonly CmlLib.Core.MinecraftLauncher launcher;

        public CmlLaunchGameLauncher(CmlLib.Core.MinecraftLauncher launcher)
        {
            this.launcher = launcher;
        }

        public ValueTask<Process> BuildProcessAsync(
            string versionName,
            MLaunchOption launchOption,
            CancellationToken cancellationToken)
        {
            return launcher.BuildProcessAsync(versionName, launchOption, cancellationToken);
        }
    }
}
