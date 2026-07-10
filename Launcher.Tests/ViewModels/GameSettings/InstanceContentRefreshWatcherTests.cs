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

using Launcher.App.ViewModels.GameSettings;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class InstanceContentRefreshWatcherTests
{
    [Fact]
    public async Task RapidDirectoryChangesAreDebouncedIntoSingleRefresh()
    {
        var monitor = new RecordingDirectoryMonitor();
        var refreshCount = 0;
        var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            () =>
            {
                Interlocked.Increment(ref refreshCount);
                refreshed.TrySetResult(true);
                return Task.CompletedTask;
            },
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(CreateInstance());
        watcher.SetEnabled(true);

        monitor.Current.Raise("Created", "first.jar");
        monitor.Current.Raise("Changed", "first.jar");

        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void SuspendAndResumeReplaceTheInfrastructureWatch()
    {
        var monitor = new RecordingDirectoryMonitor();
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Saves,
            () => Task.CompletedTask,
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(CreateInstance());
        watcher.SetEnabled(true);
        var firstWatch = monitor.Current;

        watcher.Suspend();
        watcher.Resume();

        Assert.True(firstWatch.IsDisposed);
        Assert.Equal(2, monitor.WatchCount);
        Assert.NotSame(firstWatch, monitor.Current);
    }

    [Fact]
    public async Task RefreshCoordinatorDiscardsResultFromPreviousInstance()
    {
        var monitor = new RecordingDirectoryMonitor();
        var firstSource = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSource = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<string> applied = [];
        var first = CreateInstance();
        var second = CreateInstance();
        using var coordinator = new LocalContentRefreshCoordinator<string>(
            monitor,
            InstanceDirectoryKind.Saves,
            (instance, _) => string.Equals(instance.Id, first.Id, StringComparison.Ordinal)
                ? firstSource.Task
                : secondSource.Task,
            values => applied = values,
            () => applied = [],
            _ => { },
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance);

        coordinator.SetInstance(first);
        var firstRefresh = coordinator.RefreshAsync();
        coordinator.SetInstance(second);
        var secondRefresh = coordinator.RefreshAsync();
        secondSource.SetResult(["second"]);
        await secondRefresh;
        firstSource.SetResult(["first"]);
        await firstRefresh;

        Assert.Equal(["second"], applied);
    }

    private static GameInstance CreateInstance()
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceDirectory = "instance"
        };
    }

    private sealed class RecordingDirectoryMonitor : IInstanceDirectoryMonitor
    {
        public int WatchCount { get; private set; }
        public RecordingDirectoryWatch Current { get; private set; } = new();

        public IInstanceDirectoryWatch Watch(GameInstance instance, InstanceDirectoryKind directoryKind)
        {
            WatchCount++;
            Current = new RecordingDirectoryWatch();
            return Current;
        }
    }

    private sealed class RecordingDirectoryWatch : IInstanceDirectoryWatch
    {
        public event EventHandler<InstanceDirectoryChangedEventArgs>? Changed;

        public bool IsDisposed { get; private set; }

        public void Raise(string changeType, string path)
        {
            Changed?.Invoke(this, new InstanceDirectoryChangedEventArgs(changeType, path));
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
