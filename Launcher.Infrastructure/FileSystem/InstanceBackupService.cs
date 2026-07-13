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
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Infrastructure.FileSystem;

/// <summary>
/// 通过临时归档和暂存目录创建、删除及恢复实例备份，避免失败时破坏现有实例。
/// </summary>
public sealed class InstanceBackupService : IInstanceBackupService
{
    private const string ManifestFileName = "launcher-backups.json";
    private const string DirectoryLockFileName = ".launcher-backups.lock";
    private static readonly TimeSpan DirectoryLockRetryDelay = TimeSpan.FromMilliseconds(100);
    private const string RestoreDirectoryPrefix = ".launcher-restore-";
    private const string RestoreMarkerFileName = ".launcher-restore-transaction.json";
    private const string RestoreConflictMarkerFileName = ".launcher-restore-conflict.json";
    private const string RestoreOwnerFileName = ".launcher-restore-owner.json";
    private const string RestoreStatePrepared = "prepared";
    private const string RestoreStateCommitted = "committed";
    private const int MaxRestoreArchiveEntries = 1_000_000;
    private const long RestoreFreeSpaceReserveBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<InstanceBackupService> logger;

    public InstanceBackupService(ILogger<InstanceBackupService>? logger = null)
    {
        this.logger = logger ?? NullLogger<InstanceBackupService>.Instance;
    }

    public Task<string> EnsureBackupDirectoryAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedDirectory = Path.GetFullPath(backupDirectory);
                Directory.CreateDirectory(normalizedDirectory);
                logger.LogInformation("Instance backup directory ensured. BackupDirectory={BackupDirectory}", normalizedDirectory);
                return normalizedDirectory;
            },
            cancellationToken);
    }

    public Task<int> CountBackupEntriesAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            return Task.FromResult(0);

        return CountBackupEntriesCoreAsync(backupDirectory, cancellationToken);
    }

    public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            return Task.FromResult<IReadOnlyList<InstanceBackupRecord>>([]);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var normalizedDirectory = Path.GetFullPath(backupDirectory);
                    if (!Directory.Exists(normalizedDirectory))
                        return (IReadOnlyList<InstanceBackupRecord>)[];

                    var directoryLock = GetDirectoryLock(normalizedDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    List<InstanceBackupRecord> filteredRecords;
                    try
                    {
                        await using var crossProcessLock = await AcquireBackupDirectoryLockAsync(
                                normalizedDirectory,
                                cancellationToken)
                            .ConfigureAwait(false);
                        var manifestPath = GetManifestPath(normalizedDirectory);
                        var records = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                        filteredRecords = NormalizeAndFilterRecords(normalizedDirectory, records);
                        if (filteredRecords.Count != records.Count)
                            await WriteManifestAsync(manifestPath, filteredRecords, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        directoryLock.Release();
                    }

                    logger.LogInformation(
                        "Instance backups loaded. BackupDirectory={BackupDirectory} Count={BackupCount}",
                        normalizedDirectory,
                        filteredRecords.Count);
                    return (IReadOnlyList<InstanceBackupRecord>)filteredRecords;
                }
                catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException
                                                 or ArgumentException
                                                 or NotSupportedException
                                                 or JsonException)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to load instance backups. BackupDirectory={BackupDirectory}",
                        backupDirectory);
                    return (IReadOnlyList<InstanceBackupRecord>)[];
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 在备份目录生成临时 ZIP，原子提交后更新清单；失败时删除所有半成品。
    /// </summary>
    public Task<InstanceBackupRecord> CreateBackupAsync(
        GameInstance instance,
        string backupDirectory,
        string backupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupName);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
                var normalizedInstanceDirectory = Path.GetFullPath(instance.InstanceDirectory);
                var minecraftDirectory = GetMinecraftDirectory(normalizedInstanceDirectory);

                logger.LogInformation(
                    "Creating instance backup. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupDirectory={BackupDirectory}",
                    instance.Id,
                    normalizedInstanceDirectory,
                    normalizedBackupDirectory);

                if (!Directory.Exists(normalizedInstanceDirectory))
                {
                    throw new InstanceBackupException(
                        InstanceBackupFailureReason.InstanceDirectoryNotFound,
                        "Instance directory does not exist.");
                }

                if (IsSameOrChildPath(normalizedInstanceDirectory, normalizedBackupDirectory))
                {
                    throw new InstanceBackupException(
                        InstanceBackupFailureReason.BackupDirectoryInsideInstance,
                        "Backup directory cannot be inside the instance directory.");
                }

                Directory.CreateDirectory(normalizedBackupDirectory);
                var sanitizedBackupName = NormalizeBackupName(backupName);
                var tempPath = Path.Combine(normalizedBackupDirectory, $".launcher-backup.{Guid.NewGuid():N}.tmp");
                string? finalPath = null;
                var shouldRollbackFinalPath = false;

                try
                {
                    await using (var mutationLock = await CrossProcessVersionLock.AcquireAsync(
                                     CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                                     progress: null,
                                     cancellationToken).ConfigureAwait(false))
                    {
                        await EnsureCurrentInstanceIdentityAsync(
                                normalizedInstanceDirectory,
                                instance.Id,
                                cancellationToken)
                            .ConfigureAwait(false);
                        EnsureNoReparsePoints(normalizedInstanceDirectory);
                        CreateInstanceZip(normalizedInstanceDirectory, instance.Name, tempPath, cancellationToken);
                        await EnsureCurrentInstanceIdentityAsync(
                                normalizedInstanceDirectory,
                                instance.Id,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    var directoryLock = GetDirectoryLock(normalizedBackupDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await using var crossProcessLock = await AcquireBackupDirectoryLockAsync(
                                normalizedBackupDirectory,
                                cancellationToken)
                            .ConfigureAwait(false);
                        var manifestPath = GetManifestPath(normalizedBackupDirectory);
                        var records = NormalizeAndFilterRecords(
                            normalizedBackupDirectory,
                            await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false));
                        var fileName = ResolveUniqueBackupFileName(normalizedBackupDirectory, records, sanitizedBackupName);
                        finalPath = Path.Combine(normalizedBackupDirectory, fileName);

                        File.Move(tempPath, finalPath);
                        shouldRollbackFinalPath = true;

                        var record = new InstanceBackupRecord
                        {
                            Name = backupName.Trim(),
                            FileName = fileName,
                            FullPath = finalPath,
                            SizeBytes = new FileInfo(finalPath).Length,
                            CreatedAt = DateTimeOffset.UtcNow
                        };

                        records.Add(record);
                        await WriteManifestAsync(manifestPath, records, cancellationToken).ConfigureAwait(false);
                        shouldRollbackFinalPath = false;

                        logger.LogInformation(
                            "Instance backup created. InstanceId={InstanceId} BackupFile={BackupFile}",
                            instance.Id,
                            finalPath);
                        return record;
                    }
                    catch
                    {
                        if (shouldRollbackFinalPath && finalPath is not null)
                            TryDeleteFileIfExists(finalPath);
                        throw;
                    }
                    finally
                    {
                        directoryLock.Release();
                    }
                }
                catch (Exception exception) when (exception is not InstanceBackupException)
                {
                    TryDeleteFileIfExists(tempPath);
                    logger.LogError(
                        exception,
                        "Failed to create instance backup. InstanceId={InstanceId} BackupDirectory={BackupDirectory}",
                        instance.Id,
                        normalizedBackupDirectory);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 仅允许删除备份根目录内的文件，并同步移除清单记录。
    /// </summary>
    public Task DeleteBackupAsync(
        string backupDirectory,
        string backupFullPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFullPath);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
                var normalizedBackupFullPath = Path.GetFullPath(backupFullPath);
                if (!IsSameOrChildPath(normalizedBackupDirectory, normalizedBackupFullPath))
                    throw new ArgumentException("Backup file must be inside the backup directory.", nameof(backupFullPath));

                logger.LogInformation(
                    "Deleting instance backup. BackupDirectory={BackupDirectory} BackupFile={BackupFile}",
                    normalizedBackupDirectory,
                    normalizedBackupFullPath);

                try
                {
                    var directoryLock = GetDirectoryLock(normalizedBackupDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await using var crossProcessLock = await AcquireBackupDirectoryLockAsync(
                                normalizedBackupDirectory,
                                cancellationToken)
                            .ConfigureAwait(false);
                        var manifestPath = GetManifestPath(normalizedBackupDirectory);
                        if (File.Exists(normalizedBackupFullPath))
                            File.Delete(normalizedBackupFullPath);

                        var records = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                        var remainingRecords = NormalizeAndFilterRecords(normalizedBackupDirectory, records)
                            .Where(record => !string.Equals(record.FullPath, normalizedBackupFullPath, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        await WriteManifestAsync(manifestPath, remainingRecords, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        directoryLock.Release();
                    }

                    logger.LogInformation(
                        "Instance backup deleted. BackupDirectory={BackupDirectory} BackupFile={BackupFile}",
                        normalizedBackupDirectory,
                        normalizedBackupFullPath);
                }
                catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException
                                                 or ArgumentException
                                                 or NotSupportedException
                                                 or JsonException)
                {
                    logger.LogError(
                        exception,
                        "Failed to delete instance backup. BackupDirectory={BackupDirectory} BackupFile={BackupFile}",
                        normalizedBackupDirectory,
                        normalizedBackupFullPath);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 在同级暂存目录解压并交换实例目录，交换失败时恢复原实例。
    /// </summary>
    public Task RestoreBackupAsync(
        GameInstance instance,
        string backupDirectory,
        string backupFullPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFullPath);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
                var normalizedBackupFullPath = Path.GetFullPath(backupFullPath);
                var normalizedInstanceDirectory = Path.GetFullPath(instance.InstanceDirectory);
                var minecraftDirectory = GetMinecraftDirectory(normalizedInstanceDirectory);
                if (!IsSameOrChildPath(normalizedBackupDirectory, normalizedBackupFullPath))
                    throw new ArgumentException("Backup file must be inside the backup directory.", nameof(backupFullPath));
                if (IsSameOrChildPath(normalizedInstanceDirectory, normalizedBackupDirectory))
                {
                    throw new InstanceBackupException(
                        InstanceBackupFailureReason.BackupDirectoryInsideInstance,
                        "Backup directory cannot be inside the instance directory.");
                }

                var versionsDirectory = Directory.GetParent(normalizedInstanceDirectory)!.FullName;
                var versionName = Path.GetFileName(normalizedInstanceDirectory);
                var transactionId = Guid.NewGuid().ToString("N");
                var stagingDirectory = Path.Combine(versionsDirectory, $"{RestoreDirectoryPrefix}{transactionId}");
                var extractDirectory = Path.Combine(stagingDirectory, "extract");
                var restoredDirectory = Path.Combine(stagingDirectory, "restored");
                var previousDirectory = Path.Combine(stagingDirectory, "previous");
                var markerPath = Path.Combine(stagingDirectory, RestoreMarkerFileName);
                var swapStarted = false;
                var swapCommitted = false;

                try
                {
                    string archiveRootName;
                    var directoryLock = GetDirectoryLock(normalizedBackupDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await using var crossProcessLock = await AcquireBackupDirectoryLockAsync(
                                normalizedBackupDirectory,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!File.Exists(normalizedBackupFullPath))
                            throw new FileNotFoundException("Backup file does not exist.", normalizedBackupFullPath);
                        Directory.CreateDirectory(extractDirectory);
                        archiveRootName = await ExtractBackupArchiveAsync(
                                normalizedBackupFullPath,
                                extractDirectory,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        directoryLock.Release();
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var extractedRootDirectory = Path.GetFullPath(Path.Combine(extractDirectory, archiveRootName));
                    if (!IsSameOrChildPath(extractDirectory, extractedRootDirectory)
                        || !Directory.Exists(extractedRootDirectory))
                    {
                        throw new InvalidDataException("Backup archive does not contain a valid instance root directory.");
                    }

                    await ValidateAndPrepareRestoredInstanceAsync(
                            extractedRootDirectory,
                            instance.Id,
                            versionName,
                            normalizedInstanceDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                    Directory.Move(extractedRootDirectory, restoredDirectory);
                    TryDeleteEmptyDirectory(extractDirectory);
                    await WriteRestoreOwnerAsync(restoredDirectory, transactionId, instance.Id, cancellationToken)
                        .ConfigureAwait(false);
                    var marker = new RestoreTransactionMarker(
                        1,
                        transactionId,
                        instance.Id,
                        versionName,
                        RestoreStatePrepared,
                        DateTimeOffset.UtcNow);
                    await AtomicJsonFileWriter.WriteAsync(markerPath, marker, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    await using (var mutationLock = await CrossProcessVersionLock.AcquireAsync(
                                     CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                                     progress: null,
                                     cancellationToken).ConfigureAwait(false))
                    {
                        await EnsureCurrentInstanceIdentityAsync(
                                normalizedInstanceDirectory,
                                instance.Id,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await EnsureRestoredInstanceOwnershipAsync(restoredDirectory, marker, cancellationToken)
                            .ConfigureAwait(false);

                        swapStarted = true;
                        Directory.Move(normalizedInstanceDirectory, previousDirectory);
                        try
                        {
                            Directory.Move(restoredDirectory, normalizedInstanceDirectory);
                        }
                        catch
                        {
                            if (Directory.Exists(previousDirectory)
                                && !Directory.Exists(normalizedInstanceDirectory))
                            {
                                Directory.Move(previousDirectory, normalizedInstanceDirectory);
                            }
                            throw;
                        }

                        marker = marker with { State = RestoreStateCommitted };
                        await AtomicJsonFileWriter.WriteAsync(markerPath, marker, JsonOptions, CancellationToken.None)
                            .ConfigureAwait(false);
                        swapCommitted = true;
                    }

                    DeleteOwnedRestorePrevious(previousDirectory, instance.Id);
                    File.Delete(markerPath);
                    DeleteRestoreOwnerIfOwned(normalizedInstanceDirectory, transactionId);
                    TryDeleteEmptyDirectory(stagingDirectory);
                    logger.LogInformation(
                        "Instance backup restored. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupFile={BackupFile}",
                        instance.Id,
                        normalizedInstanceDirectory,
                        normalizedBackupFullPath);
                }
                catch (Exception exception)
                {
                    try
                    {
                        if (!swapStarted)
                            DeleteLocallyOwnedRestoreStaging(stagingDirectory, versionsDirectory, transactionId);
                        else
                            await RecoverRestoreDirectoryAsync(
                                    minecraftDirectory,
                                    stagingDirectory,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                    }
                    catch (Exception recoveryException)
                    {
                        logger.LogError(
                            recoveryException,
                            "Failed to reconcile interrupted restore staging. StagingDirectory={StagingDirectory}",
                            stagingDirectory);
                    }
                    if (swapCommitted)
                    {
                        logger.LogWarning(
                            exception,
                            "Instance restore committed but cleanup was deferred to startup recovery. InstanceId={InstanceId} StagingDirectory={StagingDirectory}",
                            instance.Id,
                            stagingDirectory);
                        return;
                    }
                    if (exception is not OperationCanceledException)
                    {
                        logger.LogError(
                            exception,
                            "Failed to restore instance backup. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupFile={BackupFile}",
                            instance.Id,
                            normalizedInstanceDirectory,
                            normalizedBackupFullPath);
                    }
                    throw;
                }
            },
            cancellationToken);
    }

    public async Task RecoverPendingRestoresAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);
        var versionsDirectory = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions"));
        if (!Directory.Exists(versionsDirectory))
            return;
        foreach (var stagingDirectory in Directory.EnumerateDirectories(
                     versionsDirectory,
                     $"{RestoreDirectoryPrefix}*",
                     SearchOption.TopDirectoryOnly).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RecoverRestoreDirectoryAsync(minecraftDirectory, stagingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or ArgumentException
                                               or InvalidDataException)
            {
                logger.LogWarning(
                    exception,
                    "Failed to recover one pending instance restore; remaining transactions will continue. Directory={Directory}",
                    stagingDirectory);
            }
        }
    }

    private async Task RecoverRestoreDirectoryAsync(
        string minecraftDirectory,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var versionsDirectory = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions"));
        var normalizedStagingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDirectory));
        if (!string.Equals(
                Path.GetDirectoryName(normalizedStagingDirectory),
                versionsDirectory,
                StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(normalizedStagingDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            logger.LogWarning(
                "Restore staging was preserved because its path is unsafe. Directory={Directory}",
                normalizedStagingDirectory);
            return;
        }

        if (File.Exists(Path.Combine(normalizedStagingDirectory, RestoreConflictMarkerFileName)))
            return;
        if (!TryReadValidRestoreMarker(normalizedStagingDirectory, out var marker))
        {
            logger.LogWarning(
                "Restore staging was preserved because its transaction marker is missing or invalid. Directory={Directory}",
                normalizedStagingDirectory);
            return;
        }

        await using var mutationLock = await CrossProcessVersionLock.AcquireAsync(
            CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
            progress: null,
            cancellationToken).ConfigureAwait(false);
        var currentDirectory = Path.Combine(versionsDirectory, marker.VersionName);
        var previousDirectory = Path.Combine(normalizedStagingDirectory, "previous");
        var restoredDirectory = Path.Combine(normalizedStagingDirectory, "restored");
        var markerPath = Path.Combine(normalizedStagingDirectory, RestoreMarkerFileName);

        if (string.Equals(marker.State, RestoreStatePrepared, StringComparison.Ordinal))
        {
            if (Directory.Exists(previousDirectory))
            {
                if (!GameInstanceSettingsStore.HasIdentity(previousDirectory, marker.InstanceId))
                {
                    QuarantineRestoreMarker(normalizedStagingDirectory, "previous directory identity mismatch");
                    return;
                }
                if (!Directory.Exists(currentDirectory) && !File.Exists(currentDirectory))
                {
                    Directory.Move(previousDirectory, currentDirectory);
                    DeleteOwnedRestoredDirectory(restoredDirectory, marker);
                    File.Delete(markerPath);
                    TryDeleteEmptyDirectory(normalizedStagingDirectory);
                    logger.LogWarning(
                        "Rolled back interrupted instance restore. InstanceId={InstanceId} Directory={Directory}",
                        marker.InstanceId,
                        currentDirectory);
                    return;
                }
                if (!HasRestoreOwner(currentDirectory, marker.TransactionId, marker.InstanceId))
                {
                    QuarantineRestoreMarker(normalizedStagingDirectory, "current directory is not owned by the restore transaction");
                    return;
                }

                marker = marker with { State = RestoreStateCommitted };
                await AtomicJsonFileWriter.WriteAsync(markerPath, marker, JsonOptions, CancellationToken.None)
                    .ConfigureAwait(false);
                DeleteRestoreOwnerIfOwned(currentDirectory, marker.TransactionId);
            }
            else
            {
                if (!GameInstanceSettingsStore.HasIdentity(currentDirectory, marker.InstanceId))
                {
                    QuarantineRestoreMarker(normalizedStagingDirectory, "current instance identity mismatch before restore swap");
                    return;
                }
                DeleteOwnedRestoredDirectory(restoredDirectory, marker);
                File.Delete(markerPath);
                TryDeleteEmptyDirectory(normalizedStagingDirectory);
                return;
            }
        }

        if (!string.Equals(marker.State, RestoreStateCommitted, StringComparison.Ordinal))
        {
            QuarantineRestoreMarker(normalizedStagingDirectory, "unknown restore state");
            return;
        }
        if (!Directory.Exists(currentDirectory))
        {
            if (File.Exists(currentDirectory)
                || !Directory.Exists(previousDirectory)
                || !GameInstanceSettingsStore.HasIdentity(previousDirectory, marker.InstanceId))
            {
                QuarantineRestoreMarker(normalizedStagingDirectory, "committed instance is missing and rollback ownership is unavailable");
                return;
            }

            Directory.Move(previousDirectory, currentDirectory);
            DeleteOwnedRestoredDirectory(restoredDirectory, marker);
            File.Delete(markerPath);
            TryDeleteEmptyDirectory(normalizedStagingDirectory);
            logger.LogWarning(
                "Rolled back committed restore because the published instance was missing. InstanceId={InstanceId} Directory={Directory}",
                marker.InstanceId,
                currentDirectory);
            return;
        }
        if (!GameInstanceSettingsStore.HasIdentity(currentDirectory, marker.InstanceId)
            || !HasRestoreOwner(currentDirectory, marker.TransactionId, marker.InstanceId))
        {
            QuarantineRestoreMarker(normalizedStagingDirectory, "committed path ownership could not be proven");
            return;
        }

        DeleteOwnedRestorePrevious(previousDirectory, marker.InstanceId);
        DeleteOwnedRestoredDirectory(restoredDirectory, marker);
        File.Delete(markerPath);
        DeleteRestoreOwnerIfOwned(currentDirectory, marker.TransactionId);
        TryDeleteEmptyDirectory(normalizedStagingDirectory);
        logger.LogInformation(
            "Completed interrupted instance restore cleanup. InstanceId={InstanceId} Directory={Directory}",
            marker.InstanceId,
            currentDirectory);
    }

    private static async Task ValidateAndPrepareRestoredInstanceAsync(
        string restoredDirectory,
        string expectedInstanceId,
        string expectedVersionName,
        string finalInstanceDirectory,
        CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(restoredDirectory);
        var settingsPath = GetInstanceSettingsPath(restoredDirectory);
        GameInstance restoredInstance;
        await using (var settingsStream = File.OpenRead(settingsPath))
        {
            restoredInstance = await JsonSerializer.DeserializeAsync<GameInstance>(
                                       settingsStream,
                                       JsonOptions,
                                       cancellationToken)
                                   .ConfigureAwait(false)
                               ?? throw new InvalidDataException("Backup instance settings are empty.");
        }
        if (!string.Equals(restoredInstance.Id, expectedInstanceId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(restoredInstance.VersionName, expectedVersionName, StringComparison.Ordinal))
        {
            throw new InstanceBackupException(
                InstanceBackupFailureReason.InstanceChanged,
                "The selected backup belongs to a different instance or version name.");
        }

        var versionJsonPath = Path.Combine(restoredDirectory, $"{expectedVersionName}.json");
        await using (var versionStream = File.OpenRead(versionJsonPath))
        using (var versionJson = await JsonDocument.ParseAsync(
                   versionStream,
                   cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!versionJson.RootElement.TryGetProperty("id", out var id)
                || !string.Equals(id.GetString(), expectedVersionName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Backup version JSON identity does not match the target instance.");
            }
        }

        restoredInstance.InstanceDirectory = finalInstanceDirectory;
        await AtomicJsonFileWriter.WriteAsync(settingsPath, restoredInstance, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteRestoreOwnerAsync(
        string restoredDirectory,
        string transactionId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var ownerPath = GetRestoreOwnerPath(restoredDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ownerPath)!);
        await AtomicJsonFileWriter.WriteAsync(
                ownerPath,
                new RestoreOwnerMarker(1, transactionId, instanceId),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task EnsureRestoredInstanceOwnershipAsync(
        string restoredDirectory,
        RestoreTransactionMarker marker,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GameInstanceSettingsStore.HasIdentity(restoredDirectory, marker.InstanceId)
            || !HasRestoreOwner(restoredDirectory, marker.TransactionId, marker.InstanceId))
        {
            throw new InstanceBackupException(
                InstanceBackupFailureReason.InstanceChanged,
                "The restored directory identity changed before publication.");
        }
        return Task.CompletedTask;
    }

    private static bool TryReadValidRestoreMarker(
        string stagingDirectory,
        out RestoreTransactionMarker marker)
    {
        marker = default!;
        try
        {
            var markerPath = Path.Combine(stagingDirectory, RestoreMarkerFileName);
            if (!File.Exists(markerPath)
                || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
            var parsed = JsonSerializer.Deserialize<RestoreTransactionMarker>(File.ReadAllText(markerPath), JsonOptions);
            if (parsed is null
                || parsed.SchemaVersion != 1
                || !Guid.TryParseExact(parsed.TransactionId, "N", out _)
                || string.IsNullOrWhiteSpace(parsed.InstanceId)
                || !VersionDirectoryName.IsSafeDirectoryName(parsed.VersionName)
                || parsed.State is not (RestoreStatePrepared or RestoreStateCommitted))
            {
                return false;
            }
            if (!string.Equals(
                    Path.GetFileName(stagingDirectory),
                    $"{RestoreDirectoryPrefix}{parsed.TransactionId}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            marker = parsed;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or ArgumentException)
        {
            return false;
        }
    }

    private static bool HasRestoreOwner(string directory, string transactionId, string instanceId)
    {
        try
        {
            var path = GetRestoreOwnerPath(directory);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return false;
            var owner = JsonSerializer.Deserialize<RestoreOwnerMarker>(File.ReadAllText(path), JsonOptions);
            return owner is { SchemaVersion: 1 }
                   && string.Equals(owner.TransactionId, transactionId, StringComparison.Ordinal)
                   && string.Equals(owner.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            return false;
        }
    }

    private static void DeleteRestoreOwnerIfOwned(string directory, string transactionId)
    {
        if (!Directory.Exists(directory))
            return;
        var ownerPath = GetRestoreOwnerPath(directory);
        try
        {
            if (!File.Exists(ownerPath))
                return;
            var owner = JsonSerializer.Deserialize<RestoreOwnerMarker>(File.ReadAllText(ownerPath), JsonOptions);
            if (owner is not null && string.Equals(owner.TransactionId, transactionId, StringComparison.Ordinal))
                File.Delete(ownerPath);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
        }
    }

    private static void DeleteOwnedRestorePrevious(string previousDirectory, string expectedInstanceId)
    {
        if (!Directory.Exists(previousDirectory))
            return;
        if (!GameInstanceSettingsStore.HasIdentity(previousDirectory, expectedInstanceId))
            throw new InvalidDataException("Restore previous directory identity could not be proven.");
        DeleteTreeWithoutFollowingReparsePoints(previousDirectory);
    }

    private static void DeleteOwnedRestoredDirectory(
        string restoredDirectory,
        RestoreTransactionMarker marker)
    {
        if (!Directory.Exists(restoredDirectory))
            return;
        if (!GameInstanceSettingsStore.HasIdentity(restoredDirectory, marker.InstanceId)
            || !HasRestoreOwner(restoredDirectory, marker.TransactionId, marker.InstanceId))
        {
            throw new InvalidDataException("Restore candidate directory identity could not be proven.");
        }
        DeleteTreeWithoutFollowingReparsePoints(restoredDirectory);
    }

    private void QuarantineRestoreMarker(string stagingDirectory, string reason)
    {
        var markerPath = Path.Combine(stagingDirectory, RestoreMarkerFileName);
        var conflictPath = Path.Combine(stagingDirectory, RestoreConflictMarkerFileName);
        try
        {
            if (File.Exists(markerPath))
                File.Move(markerPath, conflictPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Failed to quarantine conflicted restore marker. Directory={Directory}", stagingDirectory);
        }
        logger.LogError(
            "Restore recovery stopped to preserve data because ownership was ambiguous. Directory={Directory} Reason={Reason}",
            stagingDirectory,
            reason);
    }

    private static string GetInstanceSettingsPath(string instanceDirectory) => Path.Combine(
        instanceDirectory,
        LauncherApplicationIdentity.StorageDirectoryName,
        "instance-settings.json");

    private static string GetRestoreOwnerPath(string directory) => Path.Combine(
        directory,
        LauncherApplicationIdentity.StorageDirectoryName,
        RestoreOwnerFileName);

    private static void EnsureNoReparsePoints(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Backup instance root must not be a reparse point.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Backup contains a reparse point: {entry}");
            if ((attributes & FileAttributes.Directory) != 0)
                EnsureNoReparsePoints(entry);
        }
    }

    private static void DeleteTreeWithoutFollowingReparsePoints(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, recursive: false);
            return;
        }
        foreach (var directory in Directory.EnumerateDirectories(path))
            DeleteTreeWithoutFollowingReparsePoints(directory);
        foreach (var file in Directory.EnumerateFiles(path))
            File.Delete(file);
        Directory.Delete(path, recursive: false);
    }

    private static void DeleteLocallyOwnedRestoreStaging(
        string stagingDirectory,
        string versionsDirectory,
        string transactionId)
    {
        if (!Directory.Exists(stagingDirectory))
            return;
        var normalizedStaging = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDirectory));
        var normalizedVersions = Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionsDirectory));
        if (!string.Equals(Path.GetDirectoryName(normalizedStaging), normalizedVersions, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(normalizedStaging),
                $"{RestoreDirectoryPrefix}{transactionId}",
                StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(normalizedStaging) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }
        DeleteTreeWithoutFollowingReparsePoints(normalizedStaging);
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or DirectoryNotFoundException)
        {
        }
    }

    private static string GetMinecraftDirectory(string normalizedInstanceDirectory)
    {
        var versionsDirectory = Directory.GetParent(normalizedInstanceDirectory)?.FullName;
        if (versionsDirectory is null
            || !string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(versionsDirectory)),
                "versions",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Instance directory must be a direct child of the Minecraft versions directory.");
        }

        return Directory.GetParent(versionsDirectory)?.FullName
            ?? throw new InvalidOperationException("Minecraft directory could not be determined from the instance path.");
    }

    private static async Task EnsureCurrentInstanceIdentityAsync(
        string instanceDirectory,
        string expectedInstanceId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(instanceDirectory))
        {
            throw new InstanceBackupException(
                InstanceBackupFailureReason.InstanceChanged,
                "The instance was moved or deleted while its backup was being restored.");
        }

        var settingsPath = Path.Combine(
            instanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "instance-settings.json");
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("Id", out var idElement)
                || !string.Equals(idElement.GetString(), expectedInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstanceBackupException(
                    InstanceBackupFailureReason.InstanceChanged,
                    "The instance directory now belongs to a different instance.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InstanceBackupException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            throw new InstanceBackupException(
                InstanceBackupFailureReason.InstanceChanged,
                "The current instance identity could not be verified.",
                exception);
        }
    }

    private async Task<int> CountBackupEntriesCoreAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var backups = await GetBackupsAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
            return backups.Count;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or ArgumentException
                                         or NotSupportedException
                                         or JsonException)
        {
            logger.LogWarning(
                exception,
                "Failed to count instance backups. BackupDirectory={BackupDirectory}",
                backupDirectory);
            return 0;
        }
    }

    private static string GetManifestPath(string backupDirectory)
    {
        return Path.Combine(backupDirectory, ManifestFileName);
    }

    private static async Task<List<InstanceBackupRecord>> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
            return [];

        await using var stream = File.OpenRead(manifestPath);
        var records = await JsonSerializer.DeserializeAsync<List<InstanceBackupRecord>>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        return records ?? [];
    }

    private static async Task WriteManifestAsync(
        string manifestPath,
        IReadOnlyList<InstanceBackupRecord> records,
        CancellationToken cancellationToken)
    {
        await AtomicJsonFileWriter.WriteAsync(manifestPath, records, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static SemaphoreSlim GetDirectoryLock(string normalizedBackupDirectory)
    {
        var lockKey = Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalizedBackupDirectory));
        return DirectoryLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
    }

    private static async Task<FileStream> AcquireBackupDirectoryLockAsync(
        string normalizedBackupDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(normalizedBackupDirectory);
        var lockPath = Path.Combine(normalizedBackupDirectory, DirectoryLockFileName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                await Task.Delay(DirectoryLockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    /// <summary>
    /// 规范化清单路径并剔除越界、重复或实际文件已缺失的记录。
    /// </summary>
    private static List<InstanceBackupRecord> NormalizeAndFilterRecords(
        string backupDirectory,
        IEnumerable<InstanceBackupRecord> records)
    {
        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.FileName))
            .Select(record =>
            {
                var fileName = Path.GetFileName(record.FileName);
                var fullPath = Path.GetFullPath(Path.Combine(backupDirectory, fileName));
                var fileInfo = new FileInfo(fullPath);
                return new InstanceBackupRecord
                {
                    Name = string.IsNullOrWhiteSpace(record.Name) ? Path.GetFileNameWithoutExtension(fileName) : record.Name,
                    FileName = fileName,
                    FullPath = fullPath,
                    SizeBytes = fileInfo.Exists ? fileInfo.Length : record.SizeBytes,
                    CreatedAt = record.CreatedAt
                };
            })
            .Where(record => File.Exists(record.FullPath))
            .OrderByDescending(record => record.CreatedAt)
            .ThenBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveUniqueBackupFileName(
        string backupDirectory,
        IReadOnlyList<InstanceBackupRecord> records,
        string backupName)
    {
        var usedNames = new HashSet<string>(
            records.Select(record => record.FileName),
            StringComparer.OrdinalIgnoreCase);
        var baseName = backupName;
        var candidate = $"{baseName}.zip";
        var index = 1;
        while (usedNames.Contains(candidate) || File.Exists(Path.Combine(backupDirectory, candidate)))
        {
            candidate = $"{baseName} ({index}).zip";
            index++;
        }

        return candidate;
    }

    private static string NormalizeBackupName(string backupName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(backupName.Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Backup" : sanitized;
    }

    /// <summary>
    /// 将实例目录写入带单一根目录的 ZIP，并逐文件检查取消。
    /// </summary>
    private static void CreateInstanceZip(
        string instanceDirectory,
        string instanceName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var rootName = Path.GetFileName(instanceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
            rootName = NormalizeBackupName(instanceName);

        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        AddDirectoryToArchive(instanceDirectory, instanceDirectory, rootName, archive, cancellationToken);
    }

    private static void AddDirectoryToArchive(
        string instanceDirectory,
        string directory,
        string rootName,
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directoryAttributes = File.GetAttributes(directory);
        if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Instance backup cannot include a reparse point: {directory}");

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Instance backup cannot include a reparse point: {entry}");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                AddDirectoryToArchive(instanceDirectory, entry, rootName, archive, cancellationToken);
                continue;
            }

            var relativePath = Path.GetRelativePath(instanceDirectory, entry)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var entryPath = $"{rootName}/{relativePath}";
            archive.CreateEntryFromFile(entry, entryPath, CompressionLevel.Optimal);
        }
    }

    private static async Task<string> ExtractBackupArchiveAsync(
        string backupFullPath,
        string extractDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(backupFullPath);
        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetKinds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        if (archive.Entries.Count > MaxRestoreArchiveEntries)
            throw new InvalidDataException("Backup archive contains too many entries.");

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archiveName = entry.FullName.Replace('\\', '/');
            if (archiveName.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(archiveName.Replace('/', Path.DirectorySeparatorChar)))
            {
                throw new InvalidDataException("Backup archive contains an absolute path.");
            }
            var segments = archiveName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
                throw new InvalidDataException("Backup archive contains a path traversal segment.");
            var normalizedEntryName = string.Join('/', segments);
            if (string.IsNullOrWhiteSpace(normalizedEntryName))
                continue;

            var separatorIndex = normalizedEntryName.IndexOf('/');
            if (separatorIndex <= 0)
                throw new InvalidDataException("Backup archive must contain the instance root directory.");

            var rootName = normalizedEntryName[..separatorIndex];
            if (rootName is "." or ".." || rootName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Backup archive contains an invalid instance root directory.");

            rootNames.Add(rootName);
            var targetPath = Path.GetFullPath(Path.Combine(
                extractDirectory,
                normalizedEntryName.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrChildPath(extractDirectory, targetPath))
                throw new InvalidDataException("Backup archive entry resolved outside the restore directory.");
            var isDirectory = archiveName.EndsWith("/", StringComparison.Ordinal);
            if (targetKinds.TryGetValue(targetPath, out var existingIsDirectory))
            {
                if (!isDirectory || !existingIsDirectory)
                    throw new InvalidDataException("Backup archive contains duplicate target paths.");
            }
            else
            {
                targetKinds[targetPath] = isDirectory;
            }
            if (!isDirectory)
                totalBytes = checked(totalBytes + entry.Length);
        }

        var root = rootNames.Count == 1
            ? rootNames.Single()
            : throw new InvalidDataException("Backup archive must contain exactly one instance root directory.");
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(extractDirectory));
        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            var availableBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
            if (totalBytes > Math.Max(0, availableBytes - RestoreFreeSpaceReserveBytes))
                throw new IOException("There is not enough free disk space to safely restore this backup.");
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archiveName = entry.FullName.Replace('\\', '/');
            var normalizedEntryName = string.Join(
                '/',
                archiveName.Split('/', StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(normalizedEntryName))
                continue;
            var targetPath = Path.GetFullPath(Path.Combine(
                extractDirectory,
                normalizedEntryName.Replace('/', Path.DirectorySeparatorChar)));
            if (archiveName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[128 * 1024];
            long copiedBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                copiedBytes = checked(copiedBytes + read);
                if (copiedBytes > entry.Length)
                    throw new InvalidDataException("Backup archive entry exceeded its declared size.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            if (copiedBytes != entry.Length)
                throw new InvalidDataException("Backup archive entry did not match its declared size.");
        }

        return root;
    }

    private static bool IsSameOrChildPath(string parentDirectory, string candidateDirectory)
    {
        var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentDirectory));
        var normalizedCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidateDirectory));
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void TryDeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record RestoreTransactionMarker(
        int SchemaVersion,
        string TransactionId,
        string InstanceId,
        string VersionName,
        string State,
        DateTimeOffset CreatedAtUtc);

    private sealed record RestoreOwnerMarker(
        int SchemaVersion,
        string TransactionId,
        string InstanceId);
}
