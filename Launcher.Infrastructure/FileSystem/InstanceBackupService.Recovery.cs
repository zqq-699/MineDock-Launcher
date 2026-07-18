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
            "Completed interrupted instance restore cleanup. InstanceId={InstanceId}",
            marker.InstanceId);
        logger.LogDebug("Interrupted instance restore cleanup directory. Directory={Directory}", currentDirectory);
    }
}
