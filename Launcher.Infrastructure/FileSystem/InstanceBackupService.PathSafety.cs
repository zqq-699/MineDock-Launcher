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
}
