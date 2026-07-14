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

using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

internal sealed class ResourceInstallTaskSession
{
    private const double ModpackArchiveWeight = 5;
    private readonly DownloadTasksPageViewModel? owner;
    private readonly string initialMessage;
    private int dependencyCount;
    private int startedDependencyCount;
    private double primaryDownloadStart = 2;
    private bool primaryDownloadActive;
    private bool modpackImportActive;

    private ResourceInstallTaskSession(DownloadTasksPageViewModel? owner, DownloadTaskItem? task, string initialMessage)
    {
        this.owner = owner;
        Task = task;
        this.initialMessage = initialMessage;
    }

    public DownloadTaskItem? Task { get; }

    public CancellationToken CancellationToken => Task?.CancellationToken ?? CancellationToken.None;

    public bool IsCancellationRequested => Task?.IsCancellationRequested == true;

    public IProgress<LauncherProgress>? Progress => Task is null
        ? null
        : new Progress<LauncherProgress>(Report);

    public static ResourceInstallTaskSession Begin(
        DownloadTasksPageViewModel? owner,
        string title,
        string subtitle,
        string initialMessage)
    {
        var task = owner?.BeginTask(title, subtitle);
        var session = new ResourceInstallTaskSession(owner, task, initialMessage);
        session.Report(new LauncherProgress(ModProgressStages.DownloadingFile, initialMessage));
        return session;
    }

    public void BeginDependencies(int count)
    {
        dependencyCount = Math.Max(0, count);
        startedDependencyCount = 0;
        primaryDownloadActive = false;
        if (dependencyCount > 0)
            Report(new LauncherProgress(ModProgressStages.DownloadingFile, initialMessage, 2));
    }

    public void ReportDependencyStarted(LauncherProgress progress)
    {
        if (dependencyCount <= 0)
        {
            Report(progress);
            return;
        }

        startedDependencyCount = Math.Min(startedDependencyCount + 1, dependencyCount);
        var completedBeforeCurrent = Math.Max(0, startedDependencyCount - 1);
        var percent = 2 + (28d * completedBeforeCurrent / dependencyCount);
        Report(progress with { Percent = percent });
    }

    public void CompleteDependencies()
    {
        if (dependencyCount > 0)
            Report(new LauncherProgress(ModProgressStages.DownloadingFile, initialMessage, 30));
    }

    public void BeginPrimaryDownload(bool hasDependencies)
    {
        primaryDownloadStart = hasDependencies ? 30 : 2;
        primaryDownloadActive = true;
        ReportToTask(new LauncherProgress(ModProgressStages.DownloadingFile, initialMessage, primaryDownloadStart));
    }

    public void BeginModpackImport()
    {
        modpackImportActive = true;
        ReportToTask(new LauncherProgress(ModProgressStages.DownloadingFile, initialMessage, 0));
    }

    public void Report(LauncherProgress progress)
    {
        if (modpackImportActive)
        {
            progress = MapModpackImportProgress(progress);
        }
        else if (primaryDownloadActive && progress.Stage is ModProgressStages.DownloadingFile)
        {
            var percent = progress.Percent is { } rawPercent
                ? primaryDownloadStart + ((96 - primaryDownloadStart) * Math.Clamp(rawPercent, 0, 100) / 100d)
                : primaryDownloadStart;
            progress = progress with { Percent = percent };
        }
        else if (primaryDownloadActive && progress.Stage is InstallProgressStages.CompletingFiles)
        {
            progress = progress with { Percent = 99 };
        }

        ReportToTask(progress);
    }

    private static LauncherProgress MapModpackImportProgress(LauncherProgress progress)
    {
        if (progress.Stage is ModProgressStages.DownloadingFile)
        {
            var archivePercent = Math.Clamp(progress.Percent ?? 0, 0, 100);
            return progress with { Percent = ModpackArchiveWeight * archivePercent / 100d };
        }

        if (progress.Percent is not { } importPercent)
            return progress;

        var normalizedImportPercent = Math.Clamp(importPercent, 0, 99);
        var percent = ModpackArchiveWeight
            + ((99 - ModpackArchiveWeight) * normalizedImportPercent / 99d);
        return progress with { Percent = percent };
    }

    private void ReportToTask(LauncherProgress progress) =>
        Task?.Report(progress with { Message = LauncherProgressTextFormatter.Format(progress) });

    public void Complete(string message) => Task?.Complete(message);

    public void Fail(string message) => Task?.Fail(message);

    public bool CompleteCancellation()
    {
        if (!IsCancellationRequested || Task is null)
            return false;
        owner?.CancelTask(Task);
        return true;
    }
}
