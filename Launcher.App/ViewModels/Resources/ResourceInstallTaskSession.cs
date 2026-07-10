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
    private readonly DownloadTasksPageViewModel? owner;

    private ResourceInstallTaskSession(DownloadTasksPageViewModel? owner, DownloadTaskItem? task)
    {
        this.owner = owner;
        Task = task;
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
        var session = new ResourceInstallTaskSession(owner, task);
        session.Report(new LauncherProgress(ModProgressStages.DownloadingFile, initialMessage));
        return session;
    }

    public void Report(LauncherProgress progress)
    {
        Task?.Report(progress with { Message = LauncherProgressTextFormatter.Format(progress) });
    }

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
