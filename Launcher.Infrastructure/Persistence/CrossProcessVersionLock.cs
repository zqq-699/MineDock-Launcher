/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

internal static class CrossProcessVersionLock
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private const string LegacyInstallLockFileName = ".bhl-install-transaction.lock";
    private const string LegacyMutationLockFileName = ".bhl-version-mutation.lock";

    public static string GetInstallCoordinationPath(string minecraftDirectory) =>
        GetPath(minecraftDirectory, "install");

    public static string GetMutationPath(string minecraftDirectory) =>
        GetPath(minecraftDirectory, "mutation");

    public static void DeleteLegacyVersionDirectoryLocks(string versionsDirectory)
    {
        TryDelete(Path.Combine(versionsDirectory, LegacyInstallLockFileName));
        TryDelete(Path.Combine(versionsDirectory, LegacyMutationLockFileName));
    }

    public static void DeleteLegacyLauncherLocks(string minecraftDirectory)
    {
        TryDelete(GetLegacyLauncherPath(minecraftDirectory, "install"));
        TryDelete(GetLegacyLauncherPath(minecraftDirectory, "mutation"));
    }

    public static async Task<FileStream> AcquireAsync(
        string path,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var queued = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                if (!queued)
                {
                    progress?.Report(new LauncherProgress(InstallProgressStages.Queue, string.Empty));
                    queued = true;
                }
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static FileStream? TryAcquire(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            return null;
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static string GetPath(string minecraftDirectory, string lockKind)
    {
        var lockDirectory = Path.Combine(
            Path.GetFullPath(minecraftDirectory),
            LauncherApplicationIdentity.StorageDirectoryName,
            "locks",
            "versions");
        Directory.CreateDirectory(lockDirectory);
        return Path.Combine(lockDirectory, $"{lockKind}.lock");
    }

    private static string GetLegacyLauncherPath(string minecraftDirectory, string lockKind)
    {
        var normalizedDirectory = Path.GetFullPath(minecraftDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory)))
            .ToLowerInvariant();
        return Path.Combine(
            AppContext.BaseDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "locks",
            "versions",
            $"{hash}.{lockKind}.lock");
    }

    private static void TryDelete(string path)
    {
        try
        {
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
