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
}
