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

using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class MinecraftVersionDirectoryCopierTests : TestTempDirectory
{
    [Fact]
    public void CopiesVersionTreeIntoNewDestination()
    {
        var source = CreateVersionDirectory("source", "1.21.4");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "library.jar"), "library");

        MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            Path.Combine(TempRoot, "source"),
            destinationGameDirectory,
            "1.21.4");

        Assert.Equal(
            "library",
            File.ReadAllText(Path.Combine(destinationGameDirectory, "versions", "1.21.4", "nested", "library.jar")));
    }

    [Fact]
    public void ExistingDestinationConflictRollsBackOnlyNewFiles()
    {
        var source = CreateVersionDirectory("source", "1.21.4");
        File.WriteAllText(Path.Combine(source, "new-file.jar"), "new");
        File.WriteAllText(Path.Combine(source, "conflict.jar"), "source");
        var destination = CreateVersionDirectory("destination", "1.21.4");
        File.WriteAllText(Path.Combine(destination, "conflict.jar"), "existing");

        Assert.Throws<IOException>(() => MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            Path.Combine(TempRoot, "source"),
            Path.Combine(TempRoot, "destination"),
            "1.21.4",
            allowExistingDestination: true));

        Assert.Equal("existing", File.ReadAllText(Path.Combine(destination, "conflict.jar")));
        Assert.False(File.Exists(Path.Combine(destination, "new-file.jar")));
    }

    [Fact]
    public void PreCanceledCopyDoesNotCreateDestination()
    {
        CreateVersionDirectory("source", "1.21.4");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            Path.Combine(TempRoot, "source"),
            destinationGameDirectory,
            "1.21.4",
            cancellationToken: cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(destinationGameDirectory, "versions", "1.21.4")));
    }

    private string CreateVersionDirectory(string gameDirectoryName, string versionName)
    {
        var directory = Path.Combine(TempRoot, gameDirectoryName, "versions", versionName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
