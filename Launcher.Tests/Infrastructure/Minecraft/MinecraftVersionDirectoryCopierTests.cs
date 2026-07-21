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
    public async Task ConcurrentSharedRuntimeCopiesPublishOneCompleteFileWithoutConflict()
    {
        var firstSource = CreateFile("first-source", new string('A', 256 * 1024));
        var secondSource = CreateFile("second-source", new string('B', 256 * 1024));
        var destination = Path.Combine(TempRoot, "destination", "library.jar");
        using var publishBarrier = new Barrier(participantCount: 2);

        var firstCopy = Task.Run(() => MinecraftSharedContentCopier.CopyFileIfMissing(
            firstSource,
            destination,
            beforePublish: (_, _) => publishBarrier.SignalAndWait()));
        var secondCopy = Task.Run(() => MinecraftSharedContentCopier.CopyFileIfMissing(
            secondSource,
            destination,
            beforePublish: (_, _) => publishBarrier.SignalAndWait()));
        var results = await Task.WhenAll(firstCopy, secondCopy);

        Assert.Single(results, copied => copied);
        var finalContent = File.ReadAllText(destination);
        Assert.True(
            finalContent == File.ReadAllText(firstSource) || finalContent == File.ReadAllText(secondSource),
            "The published file must be one complete source file.");
        Assert.Empty(EnumerateCopyTemporaryFiles(Path.GetDirectoryName(destination)!));
    }

    private string CreateFile(string directoryName, string content)
    {
        var path = Path.Combine(TempRoot, directoryName, "library.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string[] EnumerateCopyTemporaryFiles(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, ".bhl-copy-pending-*.tmp", SearchOption.AllDirectories)
            : [];
    }

    private string CreateVersionDirectory(string gameDirectoryName, string versionName)
    {
        var directory = Path.Combine(TempRoot, gameDirectoryName, "versions", versionName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
