/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class InstanceDirectoryMonitorTests : TestTempDirectory
{
    [Theory]
    [InlineData("mods", InstanceDirectoryKind.Mods)]
    [InlineData("shaderpacks", InstanceDirectoryKind.ShaderPacks)]
    public void WatchDoesNotCreateMissingContentDirectory(string directoryName, InstanceDirectoryKind kind)
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instance")).FullName;
        var monitor = new InstanceDirectoryMonitor();

        using var watch = monitor.Watch(CreateInstance(instanceDirectory), kind);

        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, directoryName)));
    }

    [Fact]
    public async Task WatchObservesTargetDirectoryCreatedLaterAndIgnoresOutsideChanges()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instance")).FullName;
        var monitor = new InstanceDirectoryMonitor();
        using var watch = monitor.Watch(CreateInstance(instanceDirectory), InstanceDirectoryKind.Mods);
        var changed = new TaskCompletionSource<InstanceDirectoryChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watch.Changed += (_, args) => changed.TrySetResult(args);

        await File.WriteAllTextAsync(Path.Combine(instanceDirectory, "outside.txt"), "outside");
        await Task.Delay(250);
        Assert.False(changed.Task.IsCompleted);

        var modsDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "mods")).FullName;
        var change = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(modsDirectory, change.FullPath, ignoreCase: true);
    }

    [Fact]
    public async Task SavesWatchObservesRecursiveChanges()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instance")).FullName;
        var saveDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves", "world", "region")).FullName;
        var monitor = new InstanceDirectoryMonitor();
        using var watch = monitor.Watch(CreateInstance(instanceDirectory), InstanceDirectoryKind.Saves);
        var expectedPath = Path.Combine(saveDirectory, "r.0.0.mca");
        var changed = new TaskCompletionSource<InstanceDirectoryChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watch.Changed += (_, args) =>
        {
            if (string.Equals(args.FullPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                changed.TrySetResult(args);
        };

        await File.WriteAllTextAsync(expectedPath, "region");

        var change = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expectedPath, change.FullPath, ignoreCase: true);
    }

    private static GameInstance CreateInstance(string directory) => new()
    {
        Id = "instance",
        InstanceDirectory = directory
    };
}
