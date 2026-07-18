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

/// <summary>
/// 将多个可并行、量纲不同的导入阶段映射为单调递增的总体百分比。
/// </summary>
internal sealed class OverallModpackImportProgress
    : IProgress<LauncherProgress>, ISpeedMeterProgress
{
    private const double ArchiveWeight = 2;
    private const double ManifestWeight = 3;
    private const double InstanceWeight = 5;
    private const double ResolveWeight = 10;
    private const double InstallWeight = 34;
    private const double DownloadWeight = 40;
    private const double OverridesWeight = 4;
    private const double CleanupWeight = 1;
    private readonly object syncRoot = new();
    private readonly IProgress<LauncherProgress> innerProgress;
    private readonly Dictionary<ModpackImportProgressBranch, ParallelBranchState> parallelBranches = [];
    private long reportSequence;
    private double lastPercent;
    private double archiveProgress;
    private double manifestProgress;
    private double instanceProgress;
    private double resolveProgress;
    private double installProgress;
    private double downloadProgress;
    private double overridesProgress;
    private double cleanupProgress;

    public OverallModpackImportProgress(IProgress<LauncherProgress> innerProgress)
    {
        this.innerProgress = innerProgress;
    }

    public SpeedMeter? SpeedMeter => SpeedMeterProgress.TryGet(innerProgress);

    public void Report(LauncherProgress value)
    {
        lock (syncRoot)
        {
            innerProgress.Report(value.DownloadSpeedTelemetry is not null
                ? value
                : MapProgress(value));
        }
    }

    public IProgress<LauncherProgress> CreateParallelBranch(ModpackImportProgressBranch branch)
    {
        lock (syncRoot)
            parallelBranches[branch] = new ParallelBranchState();
        return new ParallelBranchProgress(this, branch);
    }

    public void CompleteParallelBranch(ModpackImportProgressBranch branch)
    {
        lock (syncRoot)
        {
            if (parallelBranches.TryGetValue(branch, out var completedBranch))
                completedBranch.IsActive = false;

            var remainingProgress = parallelBranches.Values
                .Where(state => state.IsActive && state.LastProgress is not null)
                .OrderByDescending(state => state.LastSequence)
                .Select(state => state.LastProgress)
                .FirstOrDefault();
            if (remainingProgress is not null)
                innerProgress.Report(MapProgress(remainingProgress));
        }
    }

    private void ReportParallelBranch(ModpackImportProgressBranch branch, LauncherProgress value)
    {
        lock (syncRoot)
        {
            if (value.DownloadSpeedTelemetry is not null)
            {
                innerProgress.Report(value);
                return;
            }

            if (parallelBranches.TryGetValue(branch, out var state))
            {
                state.LastProgress = value;
                state.LastSequence = ++reportSequence;
            }

            innerProgress.Report(MapProgress(value));
        }
    }

    /// <summary>
    /// 把单阶段进度映射到总体权重，并保证对外百分比单调且不提前到达 100%。
    /// </summary>
    private LauncherProgress MapProgress(LauncherProgress value)
    {
        if (!TryUpdateBuckets(value, out var mappedPercent))
            return value;

        // 99% 留给调用方在全部提交与清理完成后报告成功，阶段乱序也不能让进度倒退。
        var clampedPercent = Math.Clamp(mappedPercent, lastPercent, 99);
        lastPercent = clampedPercent;
        return value with { Percent = clampedPercent };
    }

    /// <summary>
    /// 更新进度事件对应的阶段桶；未知阶段保留调用方原始百分比。
    /// </summary>
    private bool TryUpdateBuckets(LauncherProgress value, out double mappedPercent)
    {
        // 每个桶只取历史最大值，允许安装和下载分支交错报告而不造成总体进度回退。
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
            case ImportProgressStages.ProcessingPackFiles:
                downloadProgress = Math.Max(downloadProgress, normalizedPercent);
                break;
            case ImportProgressStages.CopyingOverrides:
                overridesProgress = Math.Max(overridesProgress, normalizedPercent);
                break;
            case ImportProgressStages.CleaningUp:
                cleanupProgress = Math.Max(cleanupProgress, normalizedPercent);
                break;
            case InstallProgressStages.Queue:
            case InstallProgressStages.Preparing:
            case InstallProgressStages.DownloadingLoaderInstaller:
            case InstallProgressStages.CheckingJava:
            case InstallProgressStages.DownloadingJava:
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
            (overridesProgress * OverridesWeight) +
            (cleanupProgress * CleanupWeight);
        return true;
    }

    private static bool IsMilestoneStage(string stage)
    {
        return stage is ImportProgressStages.PreparingArchive
            or ImportProgressStages.ParsingManifest
            or ImportProgressStages.CreatingInstance
            or ImportProgressStages.CopyingOverrides
            or ImportProgressStages.CleaningUp;
    }

    private static double NormalizePercent(double? percent, bool treatMissingAsComplete)
    {
        if (percent is null)
            return treatMissingAsComplete ? 1 : 0;

        return Math.Clamp(percent.Value, 0, 100) / 100d;
    }

    private sealed class ParallelBranchState
    {
        public bool IsActive { get; set; } = true;
        public LauncherProgress? LastProgress { get; set; }
        public long LastSequence { get; set; }
    }

    private sealed class ParallelBranchProgress(
        OverallModpackImportProgress owner,
        ModpackImportProgressBranch branch)
        : IProgress<LauncherProgress>, ISpeedMeterProgress
    {
        public SpeedMeter? SpeedMeter => owner.SpeedMeter;

        public void Report(LauncherProgress value) => owner.ReportParallelBranch(branch, value);
    }
}

internal enum ModpackImportProgressBranch
{
    PackFiles,
    LoaderInstall
}
