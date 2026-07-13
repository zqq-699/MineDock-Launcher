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
}
