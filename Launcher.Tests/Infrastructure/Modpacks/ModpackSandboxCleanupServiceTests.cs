/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ModpackSandboxCleanupServiceTests
{
    [Fact]
    public async Task SessionWritesMarkerAndSynchronousCleanupDeletesSandbox()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        var session = service.CreateSession(ModpackSandboxKind.ModpackVersion);

        var sandboxDirectory = session.DirectoryPath;
        var transactionId = Path.GetFileName(sandboxDirectory);
        var markerPath = Path.Combine(sandboxDirectory, ModpackSandboxCleanupService.MarkerFileName);
        using (var marker = JsonDocument.Parse(await File.ReadAllTextAsync(markerPath)))
        {
            Assert.Equal(1, marker.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(transactionId, marker.RootElement.GetProperty("TransactionId").GetString());
            Assert.Equal(nameof(ModpackSandboxKind.ModpackVersion), marker.RootElement.GetProperty("Kind").GetString());
            Assert.NotEqual(default, marker.RootElement.GetProperty("CreatedAtUtc").GetDateTimeOffset());
        }

        await File.WriteAllTextAsync(Path.Combine(sandboxDirectory, "installed.json"), "{}");
        await session.CleanupAsync(deferCleanup: false);

        Assert.False(Directory.Exists(sandboxDirectory));
    }

    [Fact]
    public async Task DeferredCleanupIsTrackedAndWaitsForDeletion()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var deletionStarted = new ManualResetEventSlim();
        using var allowDeletion = new ManualResetEventSlim();
        var service = new ModpackSandboxCleanupService(
            temporaryDirectory.Path,
            path =>
            {
                deletionStarted.Set();
                Assert.True(allowDeletion.Wait(TimeSpan.FromSeconds(5)));
                Directory.Delete(path, recursive: true);
            });
        var session = service.CreateSession(ModpackSandboxKind.InstanceVersion);
        var sandboxDirectory = session.DirectoryPath;

        await session.CleanupAsync(deferCleanup: true);
        Assert.True(deletionStarted.Wait(TimeSpan.FromSeconds(5)));

        var waitTask = service.WaitForPendingCleanupAsync();
        await Task.Delay(50);
        Assert.False(waitTask.IsCompleted);

        allowDeletion.Set();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Directory.Exists(sandboxDirectory));
    }

    [Theory]
    [InlineData(ModpackSandboxKind.ModpackVersion)]
    [InlineData(ModpackSandboxKind.InstanceVersion)]
    public async Task StartupCleanupDeletesValidAbandonedSandbox(ModpackSandboxKind kind)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var creator = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        var session = creator.CreateSession(kind);
        var sandboxDirectory = session.DirectoryPath;
        await session.DisposeAsync();

        var recovery = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        await recovery.CleanupStaleAsync();

        Assert.False(Directory.Exists(sandboxDirectory));
    }

    [Fact]
    public async Task StartupCleanupSkipsActiveSandboxAndDeletesItAfterLockIsReleased()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var creator = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        var session = creator.CreateSession(ModpackSandboxKind.ModpackVersion);
        var sandboxDirectory = session.DirectoryPath;
        var recovery = new ModpackSandboxCleanupService(temporaryDirectory.Path);

        await recovery.CleanupStaleAsync();
        Assert.True(Directory.Exists(sandboxDirectory));

        await session.DisposeAsync();
        await recovery.CleanupStaleAsync();
        Assert.False(Directory.Exists(sandboxDirectory));
    }

    [Fact]
    public async Task StartupCleanupUsesRecoveryMarkerWhenCrashLeavesDirectoryMarkerMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var creator = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        var session = creator.CreateSession(ModpackSandboxKind.ModpackVersion);
        var sandboxDirectory = session.DirectoryPath;
        File.Delete(Path.Combine(sandboxDirectory, ModpackSandboxCleanupService.MarkerFileName));
        await session.DisposeAsync();

        var recovery = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        await recovery.CleanupStaleAsync();

        Assert.False(Directory.Exists(sandboxDirectory));
    }

    [Fact]
    public async Task StartupCleanupPreservesSandboxesWithUntrustedMarkers()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = GetKindRoot(temporaryDirectory.Path, ModpackSandboxKind.ModpackVersion);
        var missingMarkerDirectory = CreateSandboxDirectory(root);
        var corruptMarkerDirectory = CreateSandboxDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(corruptMarkerDirectory, ModpackSandboxCleanupService.MarkerFileName),
            "{not-json");
        var mismatchedMarkerDirectory = CreateSandboxDirectory(root);
        WriteMarker(
            mismatchedMarkerDirectory,
            Guid.NewGuid().ToString("N"),
            ModpackSandboxKind.ModpackVersion);

        var service = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        await service.CleanupStaleAsync();

        Assert.True(Directory.Exists(missingMarkerDirectory));
        Assert.True(Directory.Exists(corruptMarkerDirectory));
        Assert.True(Directory.Exists(mismatchedMarkerDirectory));
    }

    [Fact]
    public async Task StartupCleanupContinuesAfterOneSandboxDeletionFails()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string? failedDirectory = null;
        var service = new ModpackSandboxCleanupService(
            temporaryDirectory.Path,
            path =>
            {
                if (string.Equals(path, failedDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Injected deletion failure.");
                Directory.Delete(path, recursive: true);
            });
        var failedSession = service.CreateSession(ModpackSandboxKind.ModpackVersion);
        var successfulSession = service.CreateSession(ModpackSandboxKind.ModpackVersion);
        failedDirectory = failedSession.DirectoryPath;
        var successfulDirectory = successfulSession.DirectoryPath;
        await failedSession.DisposeAsync();
        await successfulSession.DisposeAsync();

        await service.CleanupStaleAsync();

        Assert.True(Directory.Exists(failedDirectory));
        Assert.False(Directory.Exists(successfulDirectory));
    }

    [Fact]
    public async Task CleanupDoesNotFollowDirectoryReparsePointWhenSupported()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new ModpackSandboxCleanupService(temporaryDirectory.Path);
        var session = service.CreateSession(ModpackSandboxKind.ModpackVersion);
        var externalDirectory = Path.Combine(temporaryDirectory.Path, "external-target");
        Directory.CreateDirectory(externalDirectory);
        var externalFile = Path.Combine(externalDirectory, "keep.txt");
        await File.WriteAllTextAsync(externalFile, "keep");
        var link = Path.Combine(session.DirectoryPath, "linked-directory");
        try
        {
            Directory.CreateSymbolicLink(link, externalDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            await session.CleanupAsync(deferCleanup: false);
            return;
        }

        await session.CleanupAsync(deferCleanup: false);

        Assert.True(File.Exists(externalFile));
        Assert.False(Directory.Exists(session.DirectoryPath));
    }

    private static string GetKindRoot(string tempRoot, ModpackSandboxKind kind) => Path.Combine(
        tempRoot,
        kind == ModpackSandboxKind.ModpackVersion
            ? "launcher-modpack-version"
            : "launcher-instance-version");

    private static string CreateSandboxDirectory(string root)
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteMarker(string directory, string transactionId, ModpackSandboxKind kind)
    {
        File.WriteAllText(
            Path.Combine(directory, ModpackSandboxCleanupService.MarkerFileName),
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                TransactionId = transactionId,
                Kind = kind.ToString(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            }));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BlockHelm.Tests",
                nameof(ModpackSandboxCleanupServiceTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
