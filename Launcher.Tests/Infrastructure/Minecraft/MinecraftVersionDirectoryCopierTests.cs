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

    [Fact]
    public void SharedRuntimeCopyHonorsCancellationBeforeEnumeratingLargeTrees()
    {
        var sourceGameDirectory = Path.Combine(TempRoot, "source-shared");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination-shared");
        Directory.CreateDirectory(Path.Combine(sourceGameDirectory, "assets", "objects", "aa"));
        File.WriteAllText(Path.Combine(sourceGameDirectory, "assets", "objects", "aa", "asset"), "asset");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            MinecraftSharedContentCopier.CopySharedRuntimeContent(
                sourceGameDirectory,
                destinationGameDirectory,
                cancellationToken: cancellation.Token));

        Assert.False(Directory.Exists(destinationGameDirectory));
    }

    [Fact]
    public void SharedRuntimeCopyAtomicallyPublishesFileWithoutLeavingTemporaryFiles()
    {
        var sourceGameDirectory = Path.Combine(TempRoot, "source-shared");
        var destinationGameDirectory = Path.Combine(TempRoot, "destination-shared");
        var sourceFile = Path.Combine(sourceGameDirectory, "libraries", "example", "library.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "library");

        var result = MinecraftSharedContentCopier.CopySharedRuntimeContent(
            sourceGameDirectory,
            destinationGameDirectory);

        var destinationFile = Path.Combine(destinationGameDirectory, "libraries", "example", "library.jar");
        Assert.Equal("library", File.ReadAllText(destinationFile));
        Assert.Equal(1, result.LibrariesCopied);
        Assert.Empty(EnumerateCopyTemporaryFiles(destinationGameDirectory));
    }

    [Fact]
    public void SharedRuntimeCopyDoesNotOverwriteExistingFile()
    {
        var sourceFile = CreateFile("source", "source");
        var destinationFile = CreateFile("destination", "existing");

        var copied = MinecraftSharedContentCopier.CopyFileIfMissing(sourceFile, destinationFile);

        Assert.False(copied);
        Assert.Equal("existing", File.ReadAllText(destinationFile));
        Assert.Empty(EnumerateCopyTemporaryFiles(Path.GetDirectoryName(destinationFile)!));
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

    [Fact]
    public void DestinationCreatedBeforePublishWinsWithoutFailure()
    {
        var sourceFile = CreateFile("source", "source");
        var destinationFile = Path.Combine(TempRoot, "destination", "library.jar");

        var copied = MinecraftSharedContentCopier.CopyFileIfMissing(
            sourceFile,
            destinationFile,
            beforePublish: (_, destination) => File.WriteAllText(destination, "winner"));

        Assert.False(copied);
        Assert.Equal("winner", File.ReadAllText(destinationFile));
        Assert.Empty(EnumerateCopyTemporaryFiles(Path.GetDirectoryName(destinationFile)!));
    }

    [Fact]
    public void CancellationBeforePublishLeavesNoDestinationOrTemporaryFile()
    {
        var sourceFile = CreateFile("source", "source");
        var destinationFile = Path.Combine(TempRoot, "destination", "library.jar");
        using var cancellation = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => MinecraftSharedContentCopier.CopyFileIfMissing(
            sourceFile,
            destinationFile,
            cancellationToken: cancellation.Token,
            beforePublish: (_, _) => cancellation.Cancel()));

        Assert.False(File.Exists(destinationFile));
        Assert.Empty(EnumerateCopyTemporaryFiles(Path.GetDirectoryName(destinationFile)!));
    }

    [Fact]
    public void NonConflictPublishFailureIsPropagatedAndTemporaryFileIsRemoved()
    {
        var sourceFile = CreateFile("source", "source");
        var destinationFile = Path.Combine(TempRoot, "destination", "library.jar");

        Assert.Throws<IOException>(() => MinecraftSharedContentCopier.CopyFileIfMissing(
            sourceFile,
            destinationFile,
            beforePublish: (_, destination) => Directory.CreateDirectory(destination)));

        Assert.True(Directory.Exists(destinationFile));
        Assert.Empty(EnumerateCopyTemporaryFiles(Path.GetDirectoryName(destinationFile)!));
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
