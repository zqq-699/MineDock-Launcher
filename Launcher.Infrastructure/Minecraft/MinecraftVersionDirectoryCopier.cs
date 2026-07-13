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

using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftVersionDirectoryCopier
{
    public static void CopyVersionDirectory(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string versionName,
        bool allowExistingDestination = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceDirectory = Path.Combine(sourceGameDirectory, "versions", versionName);
        var destinationDirectory = Path.Combine(destinationGameDirectory, "versions", versionName);
        var destinationAlreadyExists = PrepareDestination(
            sourceDirectory,
            destinationDirectory,
            allowExistingDestination,
            $"Version directory already exists: {versionName}");
        var copiedFiles = new List<string>();
        try
        {
            CopyFiles(sourceDirectory, destinationDirectory, copiedFiles, cancellationToken);
        }
        catch
        {
            RollbackVersionDirectoryCopy(destinationAlreadyExists, destinationDirectory, copiedFiles);
            throw;
        }
    }

    public static void CopyVersionDirectoryTo(
        string sourceGameDirectory,
        string versionName,
        string destinationDirectory,
        bool allowExistingDestination = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceDirectory = Path.Combine(sourceGameDirectory, "versions", versionName);
        var destinationAlreadyExists = PrepareDestination(
            sourceDirectory,
            destinationDirectory,
            allowExistingDestination,
            $"Version output directory already exists: {destinationDirectory}");
        var copiedFiles = new List<string>();
        try
        {
            CopyFiles(sourceDirectory, destinationDirectory, copiedFiles, cancellationToken);
        }
        catch
        {
            RollbackVersionDirectoryCopyTo(destinationAlreadyExists, destinationDirectory, copiedFiles);
            throw;
        }
    }

    private static bool PrepareDestination(
        string sourceDirectory,
        string destinationDirectory,
        bool allowExistingDestination,
        string destinationExistsMessage)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory is missing: {sourceDirectory}");

        var destinationAlreadyExists = Directory.Exists(destinationDirectory);
        if (destinationAlreadyExists && !allowExistingDestination)
            throw new IOException(destinationExistsMessage);

        Directory.CreateDirectory(destinationDirectory);
        return destinationAlreadyExists;
    }

    private static void CopyFiles(
        string sourceDirectory,
        string destinationDirectory,
        ICollection<string> copiedFiles,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: false);
            copiedFiles.Add(destinationPath);
        }
    }

    private static void RollbackVersionDirectoryCopy(
        bool destinationAlreadyExists,
        string destinationDirectory,
        IEnumerable<string> copiedFiles)
    {
        if (!destinationAlreadyExists)
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);
            return;
        }

        foreach (var copiedFile in copiedFiles)
            TryDeleteCopiedFile(copiedFile);
    }

    private static void TryDeleteCopiedFile(string copiedFile)
    {
        try
        {
            if (File.Exists(copiedFile))
                File.Delete(copiedFile);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RollbackVersionDirectoryCopyTo(
        bool destinationAlreadyExists,
        string destinationDirectory,
        IEnumerable<string> copiedFiles)
    {
        foreach (var copiedFile in copiedFiles)
            DeleteCopiedFileForExplicitDestination(copiedFile, destinationAlreadyExists);

        if (!destinationAlreadyExists && Directory.Exists(destinationDirectory))
            Directory.Delete(destinationDirectory, recursive: true);
    }

    private static void DeleteCopiedFileForExplicitDestination(
        string copiedFile,
        bool destinationAlreadyExists)
    {
        try
        {
            if (File.Exists(copiedFile))
                File.Delete(copiedFile);
        }
        catch (Exception) when (destinationAlreadyExists)
        {
        }
    }
}
