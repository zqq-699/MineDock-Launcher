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

public sealed partial class InstanceBackupService
{
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
                        "Instance backup restored. InstanceId={InstanceId}",
                        instance.Id);
                    logger.LogDebug(
                        "Instance backup restore paths. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupFile={BackupFile}",
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
}
