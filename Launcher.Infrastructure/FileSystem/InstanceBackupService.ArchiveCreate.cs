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
}
