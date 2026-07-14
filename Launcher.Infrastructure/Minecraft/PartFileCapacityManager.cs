/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using System.IO;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Bounded, non-CAS accounting for resumable files.  It intentionally only
/// manages directories that have hosted a BlockHelm part file in this process;
/// no instance-directory crawl is introduced.
/// </summary>
internal static class PartFileCapacityManager
{
    internal const long DefaultCapacityBytes = 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan StaleAge = TimeSpan.FromDays(7);
    private static readonly ConcurrentDictionary<string, byte> Directories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task EnsureCapacityAsync(
        string destinationPath,
        long? expectedSize,
        CancellationToken cancellationToken)
    {
        if (!expectedSize.HasValue)
            return;
        if (expectedSize.Value > DefaultCapacityBytes)
            throw LocalFailure("The expected download size exceeds the resumable-download capacity budget.");

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw LocalFailure("The download destination has no parent directory.");
        Directories.TryAdd(Path.GetFullPath(directory), 0);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var candidateDirectory in Directories.Keys)
                CleanupStaleParts(candidateDirectory);

            var retained = Directories.Keys.Sum(GetPartBytes);
            var ownPart = destinationPath + ".part";
            var ownLength = File.Exists(ownPart) ? new FileInfo(ownPart).Length : 0;
            var requiredAdditional = Math.Max(0, expectedSize.Value - ownLength);
            if (retained + requiredAdditional > DefaultCapacityBytes)
                throw LocalFailure("The resumable-download capacity budget is exhausted.");
        }
        finally
        {
            Gate.Release();
        }
    }

    private static long GetPartBytes(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.part", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void CleanupStaleParts(string directory)
    {
        try
        {
            foreach (var partPath in Directory.EnumerateFiles(directory, "*.part", SearchOption.TopDirectoryOnly))
            {
                var updatedAt = File.GetLastWriteTimeUtc(partPath);
                if (DateTime.UtcNow - updatedAt < StaleAge)
                    continue;
                var lockPath = partPath + ".lock";
                FileStream? orphanLock = null;
                try
                {
                    orphanLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    TryDelete(partPath);
                    TryDelete(partPath + ".meta");
                }
                catch (IOException)
                {
                    continue;
                }
                finally
                {
                    orphanLock?.Dispose();
                }
                TryDelete(lockPath);
            }
            foreach (var lockPath in Directory.EnumerateFiles(directory, "*.part.lock", SearchOption.TopDirectoryOnly))
            {
                var partPath = lockPath[..^".lock".Length];
                if (File.Exists(partPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) < StaleAge)
                    continue;
                try
                {
                    using var orphanLock = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    TryDelete(partPath + ".meta");
                }
                catch (IOException)
                {
                    continue;
                }
                TryDelete(lockPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Capacity will fail explicitly if inaccessible parts cannot be reclaimed.
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DownloadLocalFileException LocalFailure(string message) =>
        new(message, new IOException(message));
}
