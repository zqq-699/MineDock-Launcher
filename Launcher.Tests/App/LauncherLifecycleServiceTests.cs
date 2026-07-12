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
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.App;

public sealed class LauncherLifecycleServiceTests
{
    [Fact]
    public async Task StateSyncDebouncesChangesAndRearmsMonitor()
    {
        var monitor = new TestLauncherStateMonitor();
        using var service = new LauncherStateSyncService(
            monitor,
            ImmediateUiDispatcher.Instance,
            debounceDelay: TimeSpan.FromMilliseconds(10));
        var settings = new LauncherSettings();
        var synchronizationCount = 0;

        service.Start(() => settings, () =>
        {
            synchronizationCount++;
            return Task.CompletedTask;
        });

        monitor.RaiseStateChanged();
        monitor.RaiseStateChanged();
        monitor.RaiseStateChanged();
        await service.WaitForPendingSyncAsync();

        Assert.Equal(1, synchronizationCount);
        Assert.Equal(2, monitor.WatchCount);
        Assert.Same(settings, monitor.LastSettings);
    }

    [Fact]
    public async Task StoppingStateSyncCancelsPendingRefresh()
    {
        var monitor = new TestLauncherStateMonitor();
        using var service = new LauncherStateSyncService(
            monitor,
            ImmediateUiDispatcher.Instance,
            debounceDelay: TimeSpan.FromMilliseconds(50));
        var synchronizationCount = 0;
        service.Start(() => new LauncherSettings(), () =>
        {
            synchronizationCount++;
            return Task.CompletedTask;
        });

        monitor.RaiseStateChanged();
        service.Stop();
        await service.WaitForPendingSyncAsync();

        Assert.Equal(0, synchronizationCount);
        Assert.Equal(1, monitor.StopCount);
    }

    [Fact]
    public async Task ShutdownIsIdempotentAndBoundedWhenBackgroundTaskDoesNotFinish()
    {
        var downloadTasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var downloadTask = downloadTasks.BeginTask("test", "test");
        var unfinishedTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        downloadTasks.TrackBackgroundTask(unfinishedTask.Task);
        var cleanup = new TestWorkspaceCleanupService();
        var installCleanup = new TestInstallCleanupService();
        var service = new LauncherShutdownService(downloadTasks, installCleanup, cleanup);

        var first = service.PrepareForExitAsync(TimeSpan.FromMilliseconds(20));
        var second = service.PrepareForExitAsync(TimeSpan.FromSeconds(1));
        await first;

        Assert.Same(first, second);
        Assert.True(downloadTask.IsCancellationRequested);
        Assert.Equal(1, installCleanup.CallCount);
        Assert.Equal(1, cleanup.CallCount);
        Assert.True(cleanup.ObservedCancellation);
    }

    [Fact]
    public async Task ShutdownRetriesInstallStagingCleanupAfterBackgroundTasksReleaseTheirLocks()
    {
        var downloadTasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var backgroundTask = Task.Delay(20);
        downloadTasks.TrackBackgroundTask(backgroundTask);
        var installCleanup = new TestInstallCleanupService(() => backgroundTask.IsCompleted);
        var workspaceCleanup = new TestWorkspaceCleanupService(completeImmediately: true);
        var service = new LauncherShutdownService(downloadTasks, installCleanup, workspaceCleanup);

        await service.PrepareForExitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, installCleanup.CallCount);
        Assert.True(installCleanup.ObservedPrerequisite);
        Assert.False(installCleanup.ObservedCancellation);
    }

    private sealed class TestLauncherStateMonitor : ILauncherStateMonitor
    {
        public event EventHandler? StateChanged;

        public int WatchCount { get; private set; }

        public int StopCount { get; private set; }

        public LauncherSettings? LastSettings { get; private set; }

        public void Watch(LauncherSettings settings)
        {
            WatchCount++;
            LastSettings = settings;
        }

        public void Stop()
        {
            StopCount++;
        }

        public void Dispose()
        {
        }

        public void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestInstallCleanupService(Func<bool>? prerequisite = null) : IInstanceInstallCleanupService
    {
        public int CallCount { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public bool ObservedPrerequisite { get; private set; }

        public Task CleanupPendingAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedCancellation = cancellationToken.IsCancellationRequested;
            ObservedPrerequisite = prerequisite?.Invoke() ?? true;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorkspaceCleanupService(bool completeImmediately = false) : IModpackWorkspaceCleanupService
    {
        public int CallCount { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public async Task CleanupAllAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (completeImmediately)
                return;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }
}
