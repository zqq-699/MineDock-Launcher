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
        Assert.Equal(1, refreshCount);
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
