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

using System.Buffers;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftSharedContentCopier
{
    private const int CopyBufferSize = 128 * 1024;
    private const string TemporaryFilePrefix = ".bhl-copy-pending-";
    private const string TemporaryFileSuffix = ".tmp";

    public static MinecraftSharedContentCopyResult CopySharedRuntimeContent(
        string sourceGameDirectory,
        string destinationGameDirectory,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var librariesCopied = CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "libraries"),
            Path.Combine(destinationGameDirectory, "libraries"),
            logger,
            cancellationToken);
        var assetIndexesCopied = CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "indexes"),
            Path.Combine(destinationGameDirectory, "assets", "indexes"),
            logger,
            cancellationToken);
        var assetObjectsCopied = CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "objects"),
            Path.Combine(destinationGameDirectory, "assets", "objects"),
            logger,
            cancellationToken);
        var logConfigsCopied = CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "log_configs"),
            Path.Combine(destinationGameDirectory, "assets", "log_configs"),
            logger,
            cancellationToken);

        logger?.LogDebug(
            "Copied shared Minecraft runtime content. Source={SourceGameDirectory} Destination={DestinationGameDirectory} LibrariesCopied={LibrariesCopied} AssetIndexesCopied={AssetIndexesCopied} AssetObjectsCopied={AssetObjectsCopied} LogConfigsCopied={LogConfigsCopied}",
            sourceGameDirectory,
            destinationGameDirectory,
            librariesCopied,
            assetIndexesCopied,
            assetObjectsCopied,
            logConfigsCopied);

        return new MinecraftSharedContentCopyResult(
            librariesCopied,
            assetIndexesCopied,
            assetObjectsCopied,
            logConfigsCopied);
    }

    public static int CopyLibraries(string sourceGameDirectory, string destinationGameDirectory, CancellationToken cancellationToken = default)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "libraries"),
            Path.Combine(destinationGameDirectory, "libraries"),
            logger: null,
            cancellationToken);
    }

    public static int CopyAssetsIndexes(string sourceGameDirectory, string destinationGameDirectory, CancellationToken cancellationToken = default)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "indexes"),
            Path.Combine(destinationGameDirectory, "assets", "indexes"),
            logger: null,
            cancellationToken);
    }

    public static int CopyAssetsObjects(string sourceGameDirectory, string destinationGameDirectory, CancellationToken cancellationToken = default)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "objects"),
            Path.Combine(destinationGameDirectory, "assets", "objects"),
            logger: null,
            cancellationToken);
    }

    public static int CopyLogConfigs(string sourceGameDirectory, string destinationGameDirectory, CancellationToken cancellationToken = default)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "log_configs"),
            Path.Combine(destinationGameDirectory, "assets", "log_configs"),
            logger: null,
            cancellationToken);
    }

    private static int CopyDirectoryContentIfMissing(
        string sourceDirectory,
        string destinationDirectory,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(sourceDirectory))
            return 0;

        var copiedFileCount = 0;
        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (CopyFileIfMissing(sourceFilePath, destinationPath, logger, cancellationToken))
                copiedFileCount++;
        }

        return copiedFileCount;
    }

    internal static bool CopyFileIfMissing(
        string sourceFilePath,
        string destinationPath,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        Action<string, string>? beforePublish = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(destinationPath))
            return false;

        var destinationFileDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationFileDirectory))
            throw new IOException($"The destination file has no parent directory: {destinationPath}");

        Directory.CreateDirectory(destinationFileDirectory);
        var temporaryPath = Path.Combine(
            destinationFileDirectory,
            $"{TemporaryFilePrefix}{Guid.NewGuid():N}{TemporaryFileSuffix}");
        var published = false;

        try
        {
            CopyToTemporaryFile(sourceFilePath, temporaryPath, cancellationToken);
            beforePublish?.Invoke(temporaryPath, destinationPath);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: false);
                published = true;
                return true;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another process atomically published the same destination first.
                return false;
            }
        }
        finally
        {
            if (!published)
                TryDeleteTemporaryFile(temporaryPath, destinationPath, logger);
        }
    }

    private static void CopyToTemporaryFile(
        string sourceFilePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        using var source = new FileStream(
            sourceFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.SequentialScan);
        using var destination = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = source.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                    break;

                destination.Write(buffer, 0, bytesRead);
            }

            destination.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDeleteTemporaryFile(
        string temporaryPath,
        string destinationPath,
        ILogger? logger)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "Failed to delete temporary shared runtime copy. TemporaryPath={TemporaryPath} DestinationPath={DestinationPath}",
                temporaryPath,
                destinationPath);
        }
    }
}

internal sealed record MinecraftSharedContentCopyResult(
    int LibrariesCopied,
    int AssetIndexesCopied,
    int AssetObjectsCopied,
    int LogConfigsCopied);
