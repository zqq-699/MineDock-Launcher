/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json;
using Launcher.Application;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Updates;

public sealed class LauncherUpdateCacheCleaner
{
    private const int DefaultDeleteAttempts = 20;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly string updatesRoot;
    private readonly ILogger<LauncherUpdateCacheCleaner>? logger;
    private readonly int deleteAttempts;
    private readonly TimeSpan retryDelay;

    public LauncherUpdateCacheCleaner(ILogger<LauncherUpdateCacheCleaner>? logger = null)
        : this(AppContext.BaseDirectory, logger, DefaultDeleteAttempts, DefaultRetryDelay)
    {
    }

    internal LauncherUpdateCacheCleaner(
        string baseDirectory,
        ILogger<LauncherUpdateCacheCleaner>? logger = null,
        int deleteAttempts = DefaultDeleteAttempts,
        TimeSpan? retryDelay = null)
    {
        updatesRoot = Path.GetFullPath(Path.Combine(
            baseDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "cache",
            "updates"));
        this.logger = logger;
        this.deleteAttempts = Math.Max(1, deleteAttempts);
        this.retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    public void CleanupStaleCache(string? currentExecutablePath)
    {
        if (!Directory.Exists(updatesRoot))
            return;

        try
        {
            var protectedDirectory = ResolveProtectedDirectory(currentExecutablePath);
            foreach (var directory in Directory.EnumerateDirectories(updatesRoot, "*", SearchOption.TopDirectoryOnly))
            {
                if (protectedDirectory is not null && PathsEqual(directory, protectedDirectory))
                    continue;

                TryDeleteDirectory(directory);
            }

            foreach (var file in Directory.EnumerateFiles(updatesRoot, "*", SearchOption.TopDirectoryOnly))
                TryDeleteFile(file);

            TryDeleteEmptyRoot();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or JsonException)
        {
            logger?.LogWarning(exception, "Failed to enumerate launcher update cache for pruning.");
        }
    }

    public void CleanupCachedUpdater(string updaterPath)
    {
        var directory = ResolveCachedVersionDirectory(updaterPath);
        if (directory is not null)
            TryDeleteDirectory(directory);
        TryDeleteEmptyRoot();
    }

    public async Task CleanupConfirmedUpdateAsync(
        string updaterPath,
        CancellationToken cancellationToken = default)
    {
        var directory = ResolveCachedVersionDirectory(updaterPath);
        if (directory is null)
        {
            logger?.LogWarning(
                "Confirmed launcher updater path is outside the update cache; cleanup skipped. Path={Path}",
                updaterPath);
            return;
        }

        for (var attempt = 1; attempt <= deleteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                TryDeleteEmptyRoot();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == deleteAttempts)
                {
                    logger?.LogWarning(
                        exception,
                        "Confirmed launcher update cache remained locked after cleanup retries. Path={Path}",
                        directory);
                    return;
                }

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string? ResolveProtectedDirectory(string? currentExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(currentExecutablePath))
            return null;

        try
        {
            var transaction = new LauncherUpdateFileOperations().ReadTransaction(
                LauncherUpdateTransaction.GetMarkerPath(currentExecutablePath));
            if (transaction is null || !transaction.HasValidDerivedPaths())
                return null;
            return ResolveCachedVersionDirectory(transaction.UpdaterPath);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or JsonException)
        {
            logger?.LogWarning(exception, "Failed to inspect pending launcher update while pruning cache.");
            return null;
        }
    }

    private string? ResolveCachedVersionDirectory(string updaterPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(updaterPath);
            var relativePath = Path.GetRelativePath(updatesRoot, fullPath);
            if (relativePath == "."
                || relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relativePath))
            {
                return null;
            }

            var firstSeparator = relativePath.IndexOf(Path.DirectorySeparatorChar);
            if (firstSeparator <= 0)
                return null;

            return Path.Combine(updatesRoot, relativePath[..firstSeparator]);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger?.LogWarning(exception, "Invalid launcher updater cache path. Path={Path}", updaterPath);
            return null;
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(exception, "Failed to prune launcher update cache directory. Path={Path}", path);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(exception, "Failed to prune launcher update cache file. Path={Path}", path);
        }
    }

    private void TryDeleteEmptyRoot()
    {
        try
        {
            if (Directory.Exists(updatesRoot)
                && !Directory.EnumerateFileSystemEntries(updatesRoot).Any())
            {
                Directory.Delete(updatesRoot);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogDebug(exception, "Launcher update cache root could not be removed yet.");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
