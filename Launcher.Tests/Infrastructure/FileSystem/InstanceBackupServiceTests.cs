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
using System.Diagnostics;
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
    public async Task CreateBackupAsyncRejectsDirectoryReparsePointWithoutArchivingExternalFiles()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
            var externalDirectory = Path.Combine(rootDirectory, "external");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(instanceDirectory);
            Directory.CreateDirectory(externalDirectory);
            File.WriteAllText(Path.Combine(externalDirectory, "secret.txt"), "outside-instance");
            CreateDirectoryReparsePoint(Path.Combine(instanceDirectory, "linked-external"), externalDirectory);
            var instance = CreateInstance(instanceDirectory);
            var service = new InstanceBackupService();

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.CreateBackupAsync(instance, backupDirectory, "Unsafe"));

            Assert.False(File.Exists(Path.Combine(backupDirectory, "Unsafe.zip")));
            Assert.Empty(Directory.Exists(backupDirectory)
                ? Directory.EnumerateFiles(backupDirectory, "*.tmp")
                : []);
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
    public async Task BackupManifestOperationsWaitForExclusiveFileLockAndHonorCancellation()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            Directory.CreateDirectory(backupDirectory);
            await using var heldLock = new FileStream(
                Path.Combine(backupDirectory, ".launcher-backups.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var service = new InstanceBackupService();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.GetBackupsAsync(backupDirectory, cancellation.Token));
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
            var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
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
    public async Task RestoreBackupAsyncRejectsBackupFromDifferentInstanceWithoutChangingCurrentInstance()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var current = CreateRestorableInstance(rootDirectory);
            File.WriteAllText(Path.Combine(current.InstanceDirectory, "current.txt"), "keep-current");
            var versionsDirectory = Path.GetDirectoryName(current.InstanceDirectory)!;
            var otherDirectory = Path.Combine(versionsDirectory, "instance-b");
            Directory.CreateDirectory(otherDirectory);
            var other = new GameInstance
            {
                Id = "instance-b-id",
                Name = "Instance B",
                VersionName = "instance-b",
                MinecraftVersion = "1.20.1",
                InstanceDirectory = otherDirectory
            };
            WriteInstanceSettings(otherDirectory, other);
            File.WriteAllText(Path.Combine(otherDirectory, "instance-b.json"), """{"id":"instance-b"}""");
            File.WriteAllText(Path.Combine(otherDirectory, "other.txt"), "other-data");
            var backupDirectory = Path.Combine(rootDirectory, "backups");
            var service = new InstanceBackupService();
            var backup = await service.CreateBackupAsync(other, backupDirectory, "Other");

            var exception = await Assert.ThrowsAsync<InstanceBackupException>(
                () => service.RestoreBackupAsync(current, backupDirectory, backup.FullPath));

            Assert.Equal(InstanceBackupFailureReason.InstanceChanged, exception.Reason);
            Assert.Equal("keep-current", File.ReadAllText(Path.Combine(current.InstanceDirectory, "current.txt")));
            Assert.False(File.Exists(Path.Combine(current.InstanceDirectory, "other.txt")));
            Assert.Empty(Directory.EnumerateDirectories(versionsDirectory, ".launcher-restore-*"));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RecoverPendingRestoresAsyncRollsBackPreparedSwapAfterCurrentWasMoved()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.GetDirectoryName(instance.InstanceDirectory)!;
            var transactionId = "a83f21c4000000000000000000000000";
            var staging = Path.Combine(versionsDirectory, $".launcher-restore-{transactionId}");
            Directory.CreateDirectory(staging);
            Directory.Move(instance.InstanceDirectory, Path.Combine(staging, "previous"));
            File.WriteAllText(Path.Combine(staging, "previous", "old.txt"), "old-data");
            CreateOwnedRestoreCandidate(staging, instance, transactionId, "restored-data");
            WriteRestoreMarker(staging, transactionId, instance, "prepared");

            await new InstanceBackupService().RecoverPendingRestoresAsync(minecraftDirectory);

            Assert.Equal("old-data", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "old.txt")));
            Assert.False(Directory.Exists(staging));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RecoverPendingRestoresAsyncCompletesCommittedCleanupWithoutRestoringPrevious()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.GetDirectoryName(instance.InstanceDirectory)!;
            var transactionId = "b94e32d5000000000000000000000000";
            var staging = Path.Combine(versionsDirectory, $".launcher-restore-{transactionId}");
            Directory.CreateDirectory(staging);
            Directory.Move(instance.InstanceDirectory, Path.Combine(staging, "previous"));
            File.WriteAllText(Path.Combine(staging, "previous", "old.txt"), "old-data");
            CreateOwnedRestoreCandidate(staging, instance, transactionId, "restored-data");
            Directory.Move(Path.Combine(staging, "restored"), instance.InstanceDirectory);
            WriteRestoreMarker(staging, transactionId, instance, "committed");

            await new InstanceBackupService().RecoverPendingRestoresAsync(minecraftDirectory);

            Assert.Equal("restored-data", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "restored.txt")));
            Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "old.txt")));
            Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "BHL", ".launcher-restore-owner.json")));
            Assert.False(Directory.Exists(staging));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RecoverPendingRestoresAsyncRollsBackCommittedRestoreWhenPublishedDirectoryIsMissing()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.GetDirectoryName(instance.InstanceDirectory)!;
            var transactionId = "c05f43e6000000000000000000000000";
            var staging = Path.Combine(versionsDirectory, $".launcher-restore-{transactionId}");
            Directory.CreateDirectory(staging);
            Directory.Move(instance.InstanceDirectory, Path.Combine(staging, "previous"));
            File.WriteAllText(Path.Combine(staging, "previous", "last-copy.txt"), "preserve");
            WriteRestoreMarker(staging, transactionId, instance, "committed");

            await new InstanceBackupService().RecoverPendingRestoresAsync(minecraftDirectory);

            Assert.Equal("preserve", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "last-copy.txt")));
            Assert.False(Directory.Exists(staging));
        }
        finally
        {
            DeleteDirectoryIfExists(rootDirectory);
        }
    }

    [Fact]
    public async Task RecoverPendingRestoresAsyncPreservesSameIdReplacementWithoutOwnerProof()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var instance = CreateRestorableInstance(rootDirectory);
            var minecraftDirectory = GetMinecraftDirectory(instance);
            var versionsDirectory = Path.GetDirectoryName(instance.InstanceDirectory)!;
            var transactionId = "d16a54f7000000000000000000000000";
            var staging = Path.Combine(versionsDirectory, $".launcher-restore-{transactionId}");
            Directory.CreateDirectory(staging);
            Directory.Move(instance.InstanceDirectory, Path.Combine(staging, "previous"));
            File.WriteAllText(Path.Combine(staging, "previous", "previous.txt"), "keep-previous");
            Directory.CreateDirectory(instance.InstanceDirectory);
            WriteInstanceSettings(instance.InstanceDirectory, instance);
            File.WriteAllText(Path.Combine(instance.InstanceDirectory, "replacement.txt"), "keep-replacement");
            WriteRestoreMarker(staging, transactionId, instance, "committed");

            await new InstanceBackupService().RecoverPendingRestoresAsync(minecraftDirectory);

            Assert.Equal("keep-replacement", File.ReadAllText(Path.Combine(instance.InstanceDirectory, "replacement.txt")));
            Assert.Equal("keep-previous", File.ReadAllText(Path.Combine(staging, "previous", "previous.txt")));
            Assert.True(File.Exists(Path.Combine(staging, ".launcher-restore-conflict.json")));
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
        var instance = new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Instance A",
            VersionName = Path.GetFileName(instanceDirectory),
            MinecraftVersion = "1.20.1",
            InstanceDirectory = instanceDirectory
        };
        if (Directory.Exists(instanceDirectory))
            WriteInstanceSettings(instanceDirectory, instance);
        return instance;
    }

    private static GameInstance CreateRestorableInstance(string rootDirectory)
    {
        var instanceDirectory = Path.Combine(rootDirectory, ".minecraft", "versions", "instance-a");
        Directory.CreateDirectory(instanceDirectory);
        var instance = CreateInstance(instanceDirectory);
        instance.VersionName = "instance-a";
        instance.MinecraftVersion = "1.20.1";
        WriteInstanceSettings(instanceDirectory, instance);
        File.WriteAllText(
            Path.Combine(instanceDirectory, "instance-a.json"),
            """{"id":"instance-a","type":"release"}""");
        return instance;
    }

    private static string GetMinecraftDirectory(GameInstance instance)
    {
        return Directory.GetParent(Directory.GetParent(instance.InstanceDirectory)!.FullName)!.FullName;
    }

    private static void WriteInstanceSettings(string instanceDirectory, string instanceId)
    {
        WriteInstanceSettings(
            instanceDirectory,
            new GameInstance
            {
                Id = instanceId,
                VersionName = Path.GetFileName(instanceDirectory),
                InstanceDirectory = instanceDirectory
            });
    }

    private static void WriteInstanceSettings(string instanceDirectory, GameInstance instance)
    {
        var metadataDirectory = Path.Combine(
            instanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName);
        Directory.CreateDirectory(metadataDirectory);
        File.WriteAllText(
            Path.Combine(metadataDirectory, "instance-settings.json"),
            JsonSerializer.Serialize(instance));
    }

    private static void CreateOwnedRestoreCandidate(
        string stagingDirectory,
        GameInstance instance,
        string transactionId,
        string contents)
    {
        var restoredDirectory = Path.Combine(stagingDirectory, "restored");
        Directory.CreateDirectory(restoredDirectory);
        var restored = new GameInstance
        {
            Id = instance.Id,
            Name = instance.Name,
            VersionName = instance.VersionName,
            MinecraftVersion = instance.MinecraftVersion,
            InstanceDirectory = instance.InstanceDirectory
        };
        WriteInstanceSettings(restoredDirectory, restored);
        File.WriteAllText(
            Path.Combine(restoredDirectory, instance.VersionName + ".json"),
            JsonSerializer.Serialize(new { id = instance.VersionName }));
        File.WriteAllText(Path.Combine(restoredDirectory, "restored.txt"), contents);
        File.WriteAllText(
            Path.Combine(restoredDirectory, "BHL", ".launcher-restore-owner.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                transactionId,
                instanceId = instance.Id
            }));
    }

    private static void WriteRestoreMarker(
        string stagingDirectory,
        string transactionId,
        GameInstance instance,
        string state)
    {
        File.WriteAllText(
            Path.Combine(stagingDirectory, ".launcher-restore-transaction.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                transactionId,
                instanceId = instance.Id,
                versionName = instance.VersionName,
                state,
                createdAtUtc = DateTimeOffset.UtcNow
            }));
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

    private static void CreateDirectoryReparsePoint(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (IOException)
        {
            // Creating a directory junction does not require the symbolic-link privilege on Windows.
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath }
        }) ?? throw new InvalidOperationException("Failed to start junction creation process.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0);
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(directory, recursive: false);
            return;
        }
        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            DeleteDirectoryIfExists(childDirectory);
        foreach (var file in Directory.EnumerateFiles(directory))
            File.Delete(file);
        Directory.Delete(directory, recursive: false);
    }
}
