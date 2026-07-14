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

using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed class CmlLibJavaRuntimeProvisioningService : IJavaRuntimeProvisioningService
{
    private const double ProgressStartPercent = 90;
    private const double ProgressEndPercent = 94;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;

    public CmlLibJavaRuntimeProvisioningService(
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<CmlLibJavaRuntimeProvisioningService>? logger = null)
    {
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger<CmlLibJavaRuntimeProvisioningService>.Instance;
    }

    public async Task EnsureForLaunchAsync(
        GameInstance instance,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var versionName = string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
        if (string.IsNullOrWhiteSpace(versionName))
            throw new InvalidOperationException("Version name is required to prepare Java runtime.");

        if (string.IsNullOrWhiteSpace(settings.MinecraftDirectory))
            throw new InvalidOperationException("Minecraft directory is required to prepare Java runtime.");

        logger.LogInformation(
            "Preparing Java runtime for launch. InstanceId={InstanceId} InstanceName={InstanceName} VersionName={VersionName} MinecraftDirectory={MinecraftDirectory} DownloadSourcePreference={DownloadSourcePreference} DownloadSpeedLimitMbPerSecond={DownloadSpeedLimitMbPerSecond}",
            instance.Id,
            instance.Name,
            versionName,
            settings.MinecraftDirectory,
            settings.DownloadSourcePreference,
            settings.DownloadSpeedLimitMbPerSecond);
        progress?.Report(new LauncherProgress(LaunchProgressStages.CheckingJava, string.Empty, ProgressStartPercent));
        var provisioningProgress = progress is null
            ? null
            : new JavaRuntimeProvisioningProgress(progress);

        try
        {
            using var downloadOperation = VanillaLoaderProvider.CreateDownloadOperationContext(
                new MinecraftPath(settings.MinecraftDirectory));
            var launcher = VanillaLoaderProvider.CreateLauncher(
                settings.MinecraftDirectory,
                provisioningProgress,
                settings.DownloadSourcePreference,
                logger,
                settings.DownloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                downloadOperation);
            VanillaLoaderProvider.AttachProgress(launcher, provisioningProgress);
            await launcher.InstallAsync(versionName, cancellationToken).ConfigureAwait(false);
            progress?.Report(new LauncherProgress(LaunchProgressStages.CheckingJava, string.Empty, ProgressEndPercent));

            logger.LogInformation(
                "Java runtime preparation completed. InstanceId={InstanceId} VersionName={VersionName} MinecraftDirectory={MinecraftDirectory}",
                instance.Id,
                versionName,
                settings.MinecraftDirectory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Java runtime preparation failed. InstanceId={InstanceId} VersionName={VersionName} MinecraftDirectory={MinecraftDirectory}",
                instance.Id,
                versionName,
                settings.MinecraftDirectory);
            throw;
        }
    }

    private sealed class JavaRuntimeProvisioningProgress : IProgress<LauncherProgress>
    {
        private readonly IProgress<LauncherProgress> inner;

        public JavaRuntimeProvisioningProgress(IProgress<LauncherProgress> inner)
        {
            this.inner = inner;
        }

        public void Report(LauncherProgress value)
        {
            var percent = value.Percent is double progressPercent
                ? ProgressStartPercent + Math.Clamp(progressPercent, 0, 100) / 100d * (ProgressEndPercent - ProgressStartPercent)
                : value.Percent;

            inner.Report(value with { Percent = percent });
        }
    }
}
