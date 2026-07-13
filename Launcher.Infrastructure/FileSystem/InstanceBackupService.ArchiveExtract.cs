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
}
