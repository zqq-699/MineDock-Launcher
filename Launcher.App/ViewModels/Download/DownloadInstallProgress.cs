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
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

internal sealed class DownloadInstallProgress : IProgress<LauncherProgress>, IDisposable
{
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(120);

    private readonly object syncRoot = new();
    private readonly DownloadTaskItem installTask;
    private readonly long installSequence;
    private readonly Action<DownloadTaskItem, LauncherProgress, long> reportProgress;
    private readonly IUiDispatcher uiDispatcher;
    private LauncherProgress? pendingProgress;
    private DateTimeOffset lastFlushedAt = DateTimeOffset.MinValue;
    private bool isFlushQueued;
    private bool isDisposed;

    public DownloadInstallProgress(
        DownloadTaskItem installTask,
        long installSequence,
        Action<DownloadTaskItem, LauncherProgress, long> reportProgress,
        IUiDispatcher uiDispatcher)
    {
        this.installTask = installTask;
        this.installSequence = installSequence;
        this.reportProgress = reportProgress;
        this.uiDispatcher = uiDispatcher;
    }

    public void Report(LauncherProgress value)
    {
        TimeSpan delay;
        lock (syncRoot)
        {
            if (isDisposed)
                return;

            pendingProgress = value;
            if (isFlushQueued)
                return;

            var elapsed = DateTimeOffset.UtcNow - lastFlushedAt;
            delay = elapsed >= UiUpdateInterval
                ? TimeSpan.Zero
                : UiUpdateInterval - elapsed;
            isFlushQueued = true;
        }

        QueueFlush(delay);
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            isDisposed = true;
            pendingProgress = null;
            isFlushQueued = false;
        }
    }

    private void QueueFlush(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            PostFlush();
            return;
        }

        _ = FlushAfterDelayAsync(delay);
    }

    private async Task FlushAfterDelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            PostFlush();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void PostFlush()
    {
        if (!uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(Flush);
            return;
        }

        Flush();
    }

    private void Flush()
    {
        LauncherProgress? progress;
        lock (syncRoot)
        {
            if (isDisposed)
                return;

            progress = pendingProgress;
            pendingProgress = null;
            lastFlushedAt = DateTimeOffset.UtcNow;
            isFlushQueued = false;
        }

        if (progress is not null)
            reportProgress(installTask, progress, installSequence);
    }
}

