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
    public void CompletingSuspensionWithoutRestartLeavesWatcherStopped()
    {
        var monitor = new RecordingDirectoryMonitor();
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            () => Task.CompletedTask,
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(CreateInstance());
        watcher.SetEnabled(true);
        var firstWatch = monitor.Current;

        watcher.Suspend();
        watcher.Resume(restart: false);

        Assert.True(firstWatch.IsDisposed);
        Assert.Equal(1, monitor.WatchCount);
    }

    [Fact]
    public void IdempotentConfigurationDoesNotReplaceActiveWatch()
    {
        var monitor = new RecordingDirectoryMonitor();
        var instance = CreateInstance();
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            () => Task.CompletedTask,
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(instance);
        watcher.SetEnabled(true);
        var firstWatch = monitor.Current;

        watcher.SetInstance(new GameInstance
        {
            Id = instance.Id,
            InstanceDirectory = instance.InstanceDirectory
        });
        watcher.SetEnabled(true);

        Assert.False(firstWatch.IsDisposed);
        Assert.Equal(1, monitor.WatchCount);
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

    [Fact]
    public async Task DisablingRefreshCoordinatorStopsWatchAndCancelsActiveRefresh()
    {
        var monitor = new RecordingDirectoryMonitor();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;
        IReadOnlyList<string> applied = [];
        using var coordinator = new LocalContentRefreshCoordinator<string>(
            monitor,
            InstanceDirectoryKind.ResourcePacks,
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref loadCount) > 1)
                    return ["loaded"];
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return [];
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }
            },
            values => applied = values,
            () => { },
            _ => { },
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance);
        coordinator.SetInstance(CreateInstance());
        coordinator.SetWatcherEnabled(true);
        var watch = monitor.Current;
        var refresh = coordinator.RefreshAsync();

        coordinator.SetWatcherEnabled(false);

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await refresh);
        Assert.True(watch.IsDisposed);

        coordinator.SetWatcherEnabled(true);
        Assert.True(await coordinator.RefreshAsync());
        Assert.Equal(["loaded"], applied);
    }

    [Fact]
    public async Task DisablingRefreshCoordinatorDiscardsLoadThatIgnoresCancellation()
    {
        var monitor = new RecordingDirectoryMonitor();
        var loadSource = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<string> applied = ["cached"];
        using var coordinator = new LocalContentRefreshCoordinator<string>(
            monitor,
            InstanceDirectoryKind.ShaderPacks,
            (_, _) => loadSource.Task,
            values => applied = values,
            () => { },
            _ => { },
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance);
        coordinator.SetInstance(CreateInstance());
        coordinator.SetWatcherEnabled(true);
        var watch = monitor.Current;
        var refresh = coordinator.RefreshAsync();

        coordinator.SetWatcherEnabled(false);
        loadSource.SetResult(["stale"]);

        Assert.False(await refresh);
        Assert.Equal(["cached"], applied);
        Assert.True(watch.IsDisposed);
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
