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

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.GameSettings;

internal sealed class InstanceContentRefreshWatcher : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(200);
    private readonly IInstanceDirectoryMonitor monitor;
    private readonly InstanceDirectoryKind directoryKind;
    private readonly Func<Task> refreshAsync;
    private readonly Action<Exception> reportFailure;
    private readonly Func<InstanceDirectoryChangedEventArgs, bool>? shouldRefresh;
    private readonly ILogger logger;
    private IInstanceDirectoryWatch? watch;
    private CancellationTokenSource? pendingRefresh;
    private GameInstance? instance;
    private bool enabled;
    private bool suspended;

    public InstanceContentRefreshWatcher(
        IInstanceDirectoryMonitor monitor,
        InstanceDirectoryKind directoryKind,
        Func<Task> refreshAsync,
        Action<Exception> reportFailure,
        ILogger logger,
        Func<InstanceDirectoryChangedEventArgs, bool>? shouldRefresh = null)
    {
        this.monitor = monitor;
        this.directoryKind = directoryKind;
        this.refreshAsync = refreshAsync;
        this.reportFailure = reportFailure;
        this.logger = logger;
        this.shouldRefresh = shouldRefresh;
    }

    public void SetInstance(GameInstance? value)
    {
        instance = value;
        ResetWatch();
    }

    public void SetEnabled(bool value)
    {
        enabled = value;
        ResetWatch();
    }

    public void Suspend()
    {
        suspended = true;
        ResetWatch();
    }

    public void Resume()
    {
        if (!suspended)
            return;
        suspended = false;
        ResetWatch();
    }

    public void Dispose()
    {
        StopWatch();
    }

    private void ResetWatch()
    {
        StopWatch();
        if (!enabled || suspended || instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        try
        {
            watch = monitor.Watch(instance, directoryKind);
            watch.Changed += Watch_Changed;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to start instance content watcher. InstanceId={InstanceId} DirectoryKind={DirectoryKind}",
                instance.Id,
                directoryKind);
        }
    }

    private void StopWatch()
    {
        CancelPendingRefresh();
        if (watch is not null)
        {
            watch.Changed -= Watch_Changed;
            watch.Dispose();
            watch = null;
        }
    }

    private void Watch_Changed(object? sender, InstanceDirectoryChangedEventArgs e)
    {
        if (shouldRefresh?.Invoke(e) == false)
            return;

        CancelPendingRefresh();
        var cancellation = new CancellationTokenSource();
        pendingRefresh = cancellation;
        _ = RefreshAfterDelayAsync(e, cancellation);
    }

    private async Task RefreshAfterDelayAsync(
        InstanceDirectoryChangedEventArgs change,
        CancellationTokenSource cancellation)
    {
        var watchedInstance = instance;
        try
        {
            await Task.Delay(RefreshDelay, cancellation.Token).ConfigureAwait(false);
            logger.LogInformation(
                "Detected instance content change. InstanceId={InstanceId} DirectoryKind={DirectoryKind} ChangeType={ChangeType} Path={Path}",
                watchedInstance?.Id,
                directoryKind,
                change.ChangeType,
                change.FullPath);
            await refreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to refresh instance content after directory change. InstanceId={InstanceId} DirectoryKind={DirectoryKind}",
                watchedInstance?.Id,
                directoryKind);
            reportFailure(exception);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref pendingRefresh, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }

    private void CancelPendingRefresh()
    {
        var cancellation = Interlocked.Exchange(ref pendingRefresh, null);
        if (cancellation is null)
            return;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
