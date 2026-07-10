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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

internal sealed class OverallModpackImportProgress(IProgress<LauncherProgress> innerProgress)
    : IProgress<LauncherProgress>
{
    private const double ArchiveWeight = 2;
    private const double ManifestWeight = 3;
    private const double InstanceWeight = 5;
    private const double ResolveWeight = 15;
    private const double InstallWeight = 44;
    private const double DownloadWeight = 26;
    private const double OverridesWeight = 4;
    private double lastPercent;
    private double archiveProgress;
    private double manifestProgress;
    private double instanceProgress;
    private double resolveProgress;
    private double installProgress;
    private double downloadProgress;
    private double overridesProgress;

    public void Report(LauncherProgress value)
    {
        innerProgress.Report(MapProgress(value));
    }

    private LauncherProgress MapProgress(LauncherProgress value)
    {
        if (!TryUpdateBuckets(value, out var mappedPercent))
            return value;

        var clampedPercent = Math.Clamp(mappedPercent, lastPercent, 99);
        lastPercent = clampedPercent;
        return value with { Percent = clampedPercent };
    }

    private bool TryUpdateBuckets(LauncherProgress value, out double mappedPercent)
    {
        var normalizedPercent = NormalizePercent(value.Percent, treatMissingAsComplete: IsMilestoneStage(value.Stage));
        switch (value.Stage)
        {
            case ImportProgressStages.PreparingArchive:
                archiveProgress = Math.Max(archiveProgress, normalizedPercent);
                break;
            case ImportProgressStages.ParsingManifest:
                archiveProgress = Math.Max(archiveProgress, 1);
                manifestProgress = Math.Max(manifestProgress, normalizedPercent);
                break;
            case ImportProgressStages.CreatingInstance:
                archiveProgress = Math.Max(archiveProgress, 1);
                manifestProgress = Math.Max(manifestProgress, 1);
                instanceProgress = Math.Max(instanceProgress, normalizedPercent);
                break;
            case ImportProgressStages.InstallingMinecraftBase:
            case ImportProgressStages.InstallingLoader:
                installProgress = Math.Max(installProgress, normalizedPercent);
                break;
            case ImportProgressStages.ResolvingPackFiles:
                resolveProgress = Math.Max(resolveProgress, normalizedPercent);
                break;
            case ImportProgressStages.DownloadingPackFiles:
                downloadProgress = Math.Max(downloadProgress, normalizedPercent);
                break;
            case ImportProgressStages.CopyingOverrides:
                overridesProgress = Math.Max(overridesProgress, normalizedPercent);
                break;
            case ImportProgressStages.CleaningUp:
                mappedPercent = 99;
                return true;
            case InstallProgressStages.Queue:
            case InstallProgressStages.Preparing:
            case InstallProgressStages.DownloadingLoaderInstaller:
            case InstallProgressStages.RunningLoaderInstaller:
            case InstallProgressStages.FinalizingVersion:
            case InstallProgressStages.CompletingFiles:
            case LaunchProgressStages.CheckingFiles:
            case LaunchProgressStages.DownloadingFiles:
                installProgress = Math.Max(installProgress, normalizedPercent);
                break;
            default:
                if (value.Percent is null)
                {
                    mappedPercent = 0;
                    return false;
                }

                mappedPercent = value.Percent.Value;
                return true;
        }

        mappedPercent =
            (archiveProgress * ArchiveWeight) +
            (manifestProgress * ManifestWeight) +
            (instanceProgress * InstanceWeight) +
            (resolveProgress * ResolveWeight) +
            (installProgress * InstallWeight) +
            (downloadProgress * DownloadWeight) +
            (overridesProgress * OverridesWeight);
        return true;
    }

    private static bool IsMilestoneStage(string stage)
    {
        return stage is ImportProgressStages.PreparingArchive
            or ImportProgressStages.ParsingManifest
            or ImportProgressStages.CreatingInstance
            or ImportProgressStages.CopyingOverrides;
    }

    private static double NormalizePercent(double? percent, bool treatMissingAsComplete)
    {
        if (percent is null)
            return treatMissingAsComplete ? 1 : 0;

        return Math.Clamp(percent.Value, 0, 100) / 100d;
    }
}
