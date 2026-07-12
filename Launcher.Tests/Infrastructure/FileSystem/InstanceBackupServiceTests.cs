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

using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Persistence;
using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class InstanceBackupServiceTests
{
    [Fact]
    public async Task CreateBackupAsyncCreatesZipWithInstanceRootAndManifestRecord()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves"));
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "options");
            File.WriteAllText(Path.Combine(instanceDirectory, "saves", "level.dat"), "level");
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();

            var record = await service.CreateBackupAsync(instance, backupDirectory, "Nightly");

            Assert.Equal("Nightly", record.Name);
            Assert.Equal("Nightly.zip", record.FileName);
            Assert.True(File.Exists(record.FullPath));
            Assert.Equal(new FileInfo(record.FullPath).Length, record.SizeBytes);
            using (var archive = ZipFile.OpenRead(record.FullPath))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName == "instance-a/options.txt");
                Assert.Contains(archive.Entries, entry => entry.FullName == "instance-a/saves/level.dat");
            }

            var manifestRecords = await ReadManifestAsync(backupDirectory);
            var manifestRecord = Assert.Single(manifestRecords);
            Assert.Equal("Nightly", manifestRecord.Name);
            Assert.Equal("Nightly.zip", manifestRecord.FileName);
            Assert.Equal(record.FullPath, manifestRecord.FullPath);
            Assert.Equal(record.SizeBytes, manifestRecord.SizeBytes);
            Assert.True(manifestRecord.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task CreateBackupAsyncCreatesUniqueFileNamesForDuplicateNames()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(instanceDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "options");
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();

            var first = await service.CreateBackupAsync(instance, backupDirectory, "Backup");
            var second = await service.CreateBackupAsync(instance, backupDirectory, "Backup");

            Assert.Equal("Backup.zip", first.FileName);
            Assert.Equal("Backup (1).zip", second.FileName);
            Assert.True(File.Exists(first.FullPath));
            Assert.True(File.Exists(second.FullPath));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task CreateBackupAsyncSerializesConcurrentDuplicateNamesAcrossServiceInstances()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(instanceDirectory);
            File.WriteAllBytes(Path.Combine(instanceDirectory, "content.bin"), new byte[256 * 1024]);
            var instance = CreateInstance(instanceDirectory);
            var services = Enumerable.Range(0, 8).Select(_ => new InstanceBackupService()).ToArray();

            var records = await Task.WhenAll(
                services.Select(
                    (service, index) => service.CreateBackupAsync(
                        instance,
                        index % 2 == 0 ? backupDirectory : backupDirectory + Path.DirectorySeparatorChar,
                        "Backup")));

            Assert.Equal(8, records.Length);
            Assert.Equal(8, records.Select(record => record.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(records, record => Assert.True(File.Exists(record.FullPath)));
            var manifestRecords = await ReadManifestAsync(backupDirectory);
            Assert.Equal(8, manifestRecords.Count);
            Assert.Equal(
                records.Select(record => record.FileName).Order(StringComparer.OrdinalIgnoreCase),
                manifestRecords.Select(record => record.FileName).Order(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task CreateBackupAsyncPreservesManifestRecordsForConcurrentDistinctNames()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(instanceDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "options");
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();

            var records = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(index => service.CreateBackupAsync(instance, backupDirectory, $"Backup {index}")));

            var manifestRecords = await ReadManifestAsync(backupDirectory);
            Assert.Equal(8, manifestRecords.Count);
            Assert.Equal(
                records.Select(record => record.FileName).Order(StringComparer.OrdinalIgnoreCase),
                manifestRecords.Select(record => record.FileName).Order(StringComparer.OrdinalIgnoreCase));
            Assert.All(manifestRecords, record => Assert.True(File.Exists(record.FullPath)));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task ConcurrentCreatesAndManifestCleanupDoNotLoseCreatedRecords()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(instanceDirectory);
            Directory.CreateDirectory(backupDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "options");
            await File.WriteAllTextAsync(
                Path.Combine(backupDirectory, "launcher-backups.json"),
                JsonSerializer.Serialize(
                    new[]
                    {
                        new InstanceBackupRecord
                        {
                            Name = "Missing",
                            FileName = "missing.zip",
                            FullPath = Path.Combine(backupDirectory, "missing.zip"),
                            CreatedAt = DateTimeOffset.UtcNow
                        }
                    }));
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();
            var createTasks = Enumerable.Range(0, 8)
                .Select(index => service.CreateBackupAsync(instance, backupDirectory, $"Concurrent {index}"))
                .ToArray();
            var readTasks = Enumerable.Range(0, 8)
                .Select(_ => service.GetBackupsAsync(backupDirectory))
                .ToArray();

            await Task.WhenAll(createTasks.Cast<Task>().Concat(readTasks));

            var createdRecords = await Task.WhenAll(createTasks);
            var manifestRecords = await ReadManifestAsync(backupDirectory);
            Assert.Equal(8, manifestRecords.Count);
            Assert.DoesNotContain(manifestRecords, record => record.FileName == "missing.zip");
            Assert.Equal(
                createdRecords.Select(record => record.FileName).Order(StringComparer.OrdinalIgnoreCase),
                manifestRecords.Select(record => record.FileName).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Empty(Directory.EnumerateFiles(backupDirectory, "*.tmp"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("nested")]
    public async Task CreateBackupAsyncFailsWhenBackupDirectoryIsInsideInstanceDirectory(string childSegment)
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = string.IsNullOrEmpty(childSegment)
                ? instanceDirectory
                : Path.Combine(instanceDirectory, childSegment);
            Directory.CreateDirectory(instanceDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "options");
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();

            var exception = await Assert.ThrowsAsync<InstanceBackupException>(
                () => service.CreateBackupAsync(instance, backupDirectory, "Bad"));

            Assert.Equal(InstanceBackupFailureReason.BackupDirectoryInsideInstance, exception.Reason);
            Assert.False(Directory.Exists(backupDirectory) && Directory.EnumerateFiles(backupDirectory, "*.zip").Any());
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task GetBackupsAsyncReturnsManifestRecordsAndFiltersMissingZipFiles()
    {
        var backupDirectory = CreateTempDirectory();
        try
        {
            var existingZip = Path.Combine(backupDirectory, "existing.zip");
            using (ZipFile.Open(existingZip, ZipArchiveMode.Create))
            {
            }

            await File.WriteAllTextAsync(
                Path.Combine(backupDirectory, "launcher-backups.json"),
                JsonSerializer.Serialize(
                    new[]
                    {
                        new InstanceBackupRecord
                        {
                            Name = "Existing",
                            FileName = "existing.zip",
                            FullPath = existingZip,
                            CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
                        },
                        new InstanceBackupRecord
                        {
                            Name = "Missing",
                            FileName = "missing.zip",
                            FullPath = Path.Combine(backupDirectory, "missing.zip"),
                            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        }
                    }));
            var service = new InstanceBackupService();

            var backups = await service.GetBackupsAsync(backupDirectory);

            var backup = Assert.Single(backups);
            Assert.Equal("Existing", backup.Name);
            Assert.Equal(new FileInfo(existingZip).Length, backup.SizeBytes);
            Assert.Equal(1, await service.CountBackupEntriesAsync(backupDirectory));
            var cleanedRecord = Assert.Single(await ReadManifestAsync(backupDirectory));
            Assert.Equal(new FileInfo(existingZip).Length, cleanedRecord.SizeBytes);
        }
        finally
        {
            DeleteDirectoryIfExists(backupDirectory);
        }
    }

    [Fact]
    public async Task DeleteBackupAsyncDeletesZipAndRemovesManifestRecord()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, "instance-a");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(instanceDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "options");
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();
            var first = await service.CreateBackupAsync(instance, backupDirectory, "First");
            var second = await service.CreateBackupAsync(instance, backupDirectory, "Second");

            await service.DeleteBackupAsync(backupDirectory, first.FullPath);

            Assert.False(File.Exists(first.FullPath));
            Assert.True(File.Exists(second.FullPath));
            var manifestRecord = Assert.Single(await ReadManifestAsync(backupDirectory));
            Assert.Equal("Second", manifestRecord.Name);
            Assert.Equal([second.FullPath], (await service.GetBackupsAsync(backupDirectory)).Select(backup => backup.FullPath));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task DeleteBackupAsyncCleansManifestWhenZipIsAlreadyMissing()
    {
        var backupDirectory = CreateTempDirectory();
        try
        {
            var missingZip = Path.Combine(backupDirectory, "missing.zip");
            await File.WriteAllTextAsync(
                Path.Combine(backupDirectory, "launcher-backups.json"),
                JsonSerializer.Serialize(
                    new[]
                    {
                        new InstanceBackupRecord
                        {
                            Name = "Missing",
                            FileName = "missing.zip",
                            FullPath = missingZip,
                            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        }
                    }));
            var service = new InstanceBackupService();

            await service.DeleteBackupAsync(backupDirectory, missingZip);

            Assert.Empty(await ReadManifestAsync(backupDirectory));
            Assert.Empty(await service.GetBackupsAsync(backupDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(backupDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncReplacesInstanceDirectoryFromBackup()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var instanceDirectory = instance.InstanceDirectory;
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves"));
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "original");
            File.WriteAllText(Path.Combine(instanceDirectory, "saves", "level.dat"), "level");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(instance, backupDirectory, "Original");
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "changed");
            File.WriteAllText(Path.Combine(instanceDirectory, "stale.txt"), "stale");
            File.Delete(Path.Combine(instanceDirectory, "saves", "level.dat"));

            await service.RestoreBackupAsync(instance, backupDirectory, backup.FullPath);

            Assert.Equal("original", File.ReadAllText(Path.Combine(instanceDirectory, "options.txt")));
            Assert.Equal("level", File.ReadAllText(Path.Combine(instanceDirectory, "saves", "level.dat")));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "stale.txt")));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncThrowsWhenBackupFileIsMissingAndKeepsInstanceDirectory()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var instanceDirectory = instance.InstanceDirectory;
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(backupDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "current");
            var service = new InstanceBackupService();

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => service.RestoreBackupAsync(instance, backupDirectory, Path.Combine(backupDirectory, "missing.zip")));

            Assert.Equal("current", File.ReadAllText(Path.Combine(instanceDirectory, "options.txt")));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncThrowsWhenArchiveStructureIsInvalidAndKeepsInstanceDirectory()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var instanceDirectory = instance.InstanceDirectory;
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(backupDirectory);
            File.WriteAllText(Path.Combine(instanceDirectory, "options.txt"), "current");
            var invalidBackupPath = Path.Combine(backupDirectory, "invalid.zip");
            using (var archive = ZipFile.Open(invalidBackupPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("loose-file.txt");
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync("loose");
            }
            var service = new InstanceBackupService();

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.RestoreBackupAsync(instance, backupDirectory, invalidBackupPath));

            Assert.Equal("current", File.ReadAllText(Path.Combine(instanceDirectory, "options.txt")));
            Assert.False(Directory.EnumerateDirectories(rootDirectory, ".launcher-restore-*").Any());
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncWaitsForMutationLockAndThenSucceedsWhenInstanceIsUnchanged()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "options.txt"), "original");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(instance, backupDirectory, "Original");
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "options.txt"), "changed");
            Task restoreTask;

            await using (await CrossProcessVersionLock.AcquireAsync(
                             CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                             progress: null,
                             CancellationToken.None))
            {
                restoreTask = service.RestoreBackupAsync(instance, backupDirectory, backup.FullPath);
                await WaitForRestoreStagingAsync(versionsDirectory);
                await Task.Delay(250);
                Assert.False(restoreTask.IsCompleted);
            }

            await restoreTask;

            Assert.Equal("original", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "options.txt")));
            Assert.Empty(Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncDoesNotRepublishOldPathWhenRenameWinsMutationLock()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
            var renamedDirectory = Path.Combine(versionsDirectory, "renamed");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "options.txt"), "original");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(instance, backupDirectory, "Original");
            Task restoreTask;

            await using (await CrossProcessVersionLock.AcquireAsync(
                             CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                             progress: null,
                             CancellationToken.None))
            {
                restoreTask = service.RestoreBackupAsync(instance, backupDirectory, backup.FullPath);
                await WaitForRestoreStagingAsync(versionsDirectory);
                Directory.Move(instance.InstanceDirectory, renamedDirectory);
            }

            var exception = await Assert.ThrowsAsync<InstanceBackupException>(() => restoreTask);

            Assert.Equal(InstanceBackupFailureReason.InstanceChanged, exception.Reason);
            Assert.False(Directory.Exists(instance.InstanceDirectory));
            Assert.True(Directory.Exists(renamedDirectory));
            Assert.Empty(Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncDoesNotResurrectInstanceWhenDeleteWinsMutationLock()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
            var deletedDirectory = Path.Combine(versionsDirectory, ".bhl-delete-pending-instance-a-test");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "options.txt"), "original");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(instance, backupDirectory, "Original");
            Task restoreTask;

            await using (await CrossProcessVersionLock.AcquireAsync(
                             CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                             progress: null,
                             CancellationToken.None))
            {
                restoreTask = service.RestoreBackupAsync(instance, backupDirectory, backup.FullPath);
                await WaitForRestoreStagingAsync(versionsDirectory);
                Directory.Move(instance.InstanceDirectory, deletedDirectory);
            }

            var exception = await Assert.ThrowsAsync<InstanceBackupException>(() => restoreTask);

            Assert.Equal(InstanceBackupFailureReason.InstanceChanged, exception.Reason);
            Assert.False(Directory.Exists(instance.InstanceDirectory));
            Assert.True(Directory.Exists(deletedDirectory));
            Assert.Empty(Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncDoesNotOverwriteSameNameReplacementInstance()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
            var replacedDirectory = Path.Combine(versionsDirectory, "replaced-original");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "options.txt"), "original");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(instance, backupDirectory, "Original");
            Task restoreTask;

            await using (await CrossProcessVersionLock.AcquireAsync(
                             CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                             progress: null,
                             CancellationToken.None))
            {
                restoreTask = service.RestoreBackupAsync(instance, backupDirectory, backup.FullPath);
                await WaitForRestoreStagingAsync(versionsDirectory);
                Directory.Move(instance.InstanceDirectory, replacedDirectory);
                Directory.CreateDirectory(instance.InstanceDirectory);
                WriteInstanceSettings(instance.InstanceDirectory, "replacement");
                File.WriteAllText(Path.Combine(instance.InstanceDirectory, "replacement.txt"), "keep");
            }

            var exception = await Assert.ThrowsAsync<InstanceBackupException>(() => restoreTask);

            Assert.Equal(InstanceBackupFailureReason.InstanceChanged, exception.Reason);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "replacement.txt")));
            Assert.True(Directory.Exists(replacedDirectory));
            Assert.Empty(Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsyncCancellationWhileWaitingForMutationLockKeepsInstanceAndCleansStaging()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "options.txt"), "current");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(instance, backupDirectory, "Current");
            using var cancellation = new CancellationTokenSource();

            await using (await CrossProcessVersionLock.AcquireAsync(
                             CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                             progress: null,
                             CancellationToken.None))
            {
                var restoreTask = service.RestoreBackupAsync(
                    instance,
                    backupDirectory,
                    backup.FullPath,
                    cancellation.Token);
                await WaitForRestoreStagingAsync(versionsDirectory);
                await Task.Delay(250);
                Assert.False(restoreTask.IsCompleted);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restoreTask);
            }

            Assert.Equal("current", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "options.txt")));
            Assert.Empty(Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task EnsureBackupDirectoryAsyncCreatesDirectoryAndReturnsNormalizedPath()
    {
        var parentDirectory = CreateTempDirectory();
        var backupDirectory = Path.Combine(parentDirectory, "backups");
        try
        {
            var service = new InstanceBackupService();

            var normalizedDirectory = await service.EnsureBackupDirectoryAsync(backupDirectory);

            Assert.Equal(Path.GetFullPath(backupDirectory), normalizedDirectory);
            Assert.True(Directory.Exists(normalizedDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(parentDirectory);
        }
    }

    private static GameInstance CreateInstance(string instanceDirectory)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Instance A",
            InstanceDirectory = instanceDirectory
        };
    }

    private static GameInstance CreateRestorableInstance(string rootDirectory)
    {
        var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
        Directory.CreateDirectory(instanceDirectory);
        var instance = CreateInstance(instanceDirectory);
        WriteInstanceSettings(instanceDirectory, instance.Id);
        return instance;
    }

    private static string GetMinecraftDirectory(GameInstance instance)
    {
        return Directory.GetParent(Directory.GetParent(instance.InstanceDirectory)!.FullName)!.FullName;
    }

    private static void WriteInstanceSettings(string instanceDirectory, string instanceId)
    {
        var metadataDirectory = Path.Combine(
            instanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName);
        Directory.CreateDirectory(metadataDirectory);
        File.WriteAllText(
            Path.Combine(metadataDirectory, "instance-settings.json"),
            JsonSerializer.Serialize(new GameInstance { Id = instanceId }));
    }

    private static async Task WaitForRestoreStagingAsync(string versionsDirectory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*").Any())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<IReadOnlyList<InstanceBackupRecord>> ReadManifestAsync(string backupDirectory)
    {
        await using var stream = File.OpenRead(Path.Combine(backupDirectory, "launcher-backups.json"));
        return await JsonSerializer.DeserializeAsync<IReadOnlyList<InstanceBackupRecord>>(stream)
            ?? [];
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
