/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.ComponentModel;
using System.IO;
using System.Security;
using Launcher.Application.Services;
using Launcher.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Persistence;

internal sealed class VersionDeletionManager
{
    private const int MaxStageAttempts = 3;
    private readonly VersionDirectoryManager directoryManager;
    private readonly ILogger logger;
    private readonly Func<Guid> guidFactory;
    private readonly Action<string, string> moveDirectory;
    private readonly Action<string, string>? beforeMove;
    private readonly bool useIdentitySafeMove;
    private readonly Action<string> recycleDirectory;
    private readonly Action<string, bool> deleteDirectory;

    public VersionDeletionManager(
        VersionDirectoryManager directoryManager,
        ILogger logger,
        Func<Guid>? guidFactory = null,
        Action<string, string>? moveDirectory = null,
        Action<string, bool>? deleteDirectory = null,
        Action<string>? recycleDirectory = null,
        Action<string, string>? beforeMove = null)
    {
        this.directoryManager = directoryManager;
        this.logger = logger;
        this.guidFactory = guidFactory ?? Guid.NewGuid;
        this.moveDirectory = moveDirectory ?? Directory.Move;
        this.beforeMove = beforeMove;
        useIdentitySafeMove = moveDirectory is null;
        this.recycleDirectory = recycleDirectory ?? WindowsRecycleBin.MoveDirectory;
        this.deleteDirectory = deleteDirectory ?? Directory.Delete;
    }

    public async Task<string> StageAsync(
        string minecraftDirectory,
        string versionName,
        string expectedInstanceId,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, versionName);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory not found: {sourceDirectory}");

        var versionsDirectory = GetVersionsDirectory(minecraftDirectory);
        var markerPath = PendingInstanceDeletionDirectory.GetMarkerPath(sourceDirectory);
        for (var attempt = 1; attempt <= MaxStageAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionId = guidFactory().ToString("N");
            var suffix = transactionId[..8].ToLowerInvariant();
            var stagedDirectory = Path.GetFullPath(Path.Combine(
                versionsDirectory,
                $"{PendingInstanceDeletionDirectory.Prefix}{versionName}-{suffix}"));
            EnsureDirectChild(versionsDirectory, stagedDirectory);

            if (PathExists(stagedDirectory))
            {
                LogCollision(versionName, stagedDirectory, attempt);
                continue;
            }

            try
            {
                await AtomicJsonFileWriter.WriteAsync(
                    markerPath,
                    new PendingInstanceDeletionMarker(
                        2,
                        transactionId,
                        versionName,
                        expectedInstanceId,
                        DateTimeOffset.UtcNow),
                    PendingInstanceDeletionDirectory.MarkerJsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (!GameInstanceSettingsStore.HasIdentity(sourceDirectory, expectedInstanceId))
                {
                    throw new GameInstanceMutationConflictException(expectedInstanceId, versionName);
                }
                if (useIdentitySafeMove)
                {
                    try
                    {
                        WindowsDirectoryHandleMover.MoveOwnedDirectory(
                            sourceDirectory,
                            stagedDirectory,
                            () => GameInstanceSettingsStore.HasIdentity(sourceDirectory, expectedInstanceId),
                            beforeMove is null ? null : () => beforeMove(sourceDirectory, stagedDirectory));
                    }
                    catch (InvalidOperationException)
                    {
                        throw new GameInstanceMutationConflictException(expectedInstanceId, versionName);
                    }
                }
                else
                {
                    beforeMove?.Invoke(sourceDirectory, stagedDirectory);
                    moveDirectory(sourceDirectory, stagedDirectory);
                }
                if (!GameInstanceSettingsStore.HasIdentity(stagedDirectory, expectedInstanceId))
                {
                    QuarantineMarker(stagedDirectory);
                    throw new GameInstanceMutationConflictException(expectedInstanceId, versionName);
                }
                logger.LogInformation(
                    "Version directory staged for deletion. VersionName={VersionName} StagedDirectory={StagedDirectory}",
                    versionName,
                    stagedDirectory);
                return stagedDirectory;
            }
            catch (IOException) when (Directory.Exists(sourceDirectory) && PathExists(stagedDirectory))
            {
                LogCollision(versionName, stagedDirectory, attempt);
                TryDeleteOwnedPreparationMarker(sourceDirectory, markerPath, expectedInstanceId);
            }
            catch
            {
                TryDeleteOwnedPreparationMarker(sourceDirectory, markerPath, expectedInstanceId);
                throw;
            }
        }

        TryDeleteOwnedPreparationMarker(sourceDirectory, markerPath, expectedInstanceId);
        throw new IOException(
            $"Unable to stage version directory for deletion after {MaxStageAttempts} destination collisions: {sourceDirectory}");
    }

    public bool TryDelete(string minecraftDirectory, string stagedDirectory)
    {
        var versionsDirectory = GetVersionsDirectory(minecraftDirectory);
        var normalizedStagedDirectory = Path.GetFullPath(stagedDirectory);
        EnsureDirectChild(versionsDirectory, normalizedStagedDirectory);
        if (!PendingInstanceDeletionDirectory.IsPending(normalizedStagedDirectory))
            throw new ArgumentException("The directory is not a pending instance deletion directory.", nameof(stagedDirectory));

        if (!Directory.Exists(normalizedStagedDirectory))
            return true;
        if (File.Exists(Path.Combine(
                normalizedStagedDirectory,
                PendingInstanceDeletionDirectory.AbortedMarkerFileName)))
        {
            logger.LogWarning(
                "Pending deletion directory was preserved because its transaction was aborted. StagedDirectory={StagedDirectory}",
                normalizedStagedDirectory);
            return false;
        }
        if (!PendingInstanceDeletionDirectory.TryReadValidMarker(normalizedStagedDirectory, out var marker))
        {
            logger.LogWarning(
                "Pending deletion directory was preserved because its transaction marker is missing or invalid. StagedDirectory={StagedDirectory}",
                normalizedStagedDirectory);
            return false;
        }
        if (marker.SchemaVersion == 2
            && !GameInstanceSettingsStore.HasIdentity(normalizedStagedDirectory, marker.InstanceId!))
        {
            QuarantineMarker(normalizedStagedDirectory);
            logger.LogError(
                "Pending deletion directory was preserved because its instance identity changed. ExpectedInstanceId={ExpectedInstanceId} StagedDirectory={StagedDirectory}",
                marker.InstanceId,
                normalizedStagedDirectory);
            return false;
        }

        try
        {
            recycleDirectory(normalizedStagedDirectory);
            logger.LogInformation(
                "Staged version directory moved to recycle bin. StagedDirectory={StagedDirectory}",
                normalizedStagedDirectory);
            return true;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to move staged version directory to recycle bin; permanent deletion will be attempted. StagedDirectory={StagedDirectory}",
                normalizedStagedDirectory);
        }

        if (!CanDeleteStagedDirectory(normalizedStagedDirectory))
            return false;

        try
        {
            deleteDirectory(normalizedStagedDirectory, true);
            logger.LogInformation(
                "Staged version directory permanently deleted. StagedDirectory={StagedDirectory}",
                normalizedStagedDirectory);
            return true;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to delete staged version directory; it will be retried on startup. StagedDirectory={StagedDirectory}",
                normalizedStagedDirectory);
            return false;
        }
    }

    public void CleanupPending(string minecraftDirectory, CancellationToken cancellationToken)
    {
        var versionsDirectory = GetVersionsDirectory(minecraftDirectory);
        if (!Directory.Exists(versionsDirectory))
            return;

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(versionsDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(PendingInstanceDeletionDirectory.IsPending)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            logger.LogWarning(
                exception,
                "Failed to enumerate pending instance deletion directories. VersionsDirectory={VersionsDirectory}",
                versionsDirectory);
            return;
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(minecraftDirectory, directory);
        }
    }

    private void LogCollision(string versionName, string stagedDirectory, int attempt)
    {
        logger.LogWarning(
            "Pending deletion directory collision. VersionName={VersionName} StagedDirectory={StagedDirectory} Attempt={Attempt} MaxAttempts={MaxAttempts}",
            versionName,
            stagedDirectory,
            attempt,
            MaxStageAttempts);
    }

    private void TryDeletePreparationMarker(string markerPath)
    {
        try
        {
            File.Delete(markerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            logger.LogWarning(
                exception,
                "Failed to remove pending deletion preparation marker after staging failed. MarkerPath={MarkerPath}",
                markerPath);
        }
    }

    private void TryDeleteOwnedPreparationMarker(
        string sourceDirectory,
        string markerPath,
        string expectedInstanceId)
    {
        if (GameInstanceSettingsStore.HasIdentity(sourceDirectory, expectedInstanceId))
            TryDeletePreparationMarker(markerPath);
    }

    private void QuarantineMarker(string directory)
    {
        var markerPath = PendingInstanceDeletionDirectory.GetMarkerPath(directory);
        var abortedPath = Path.Combine(directory, PendingInstanceDeletionDirectory.AbortedMarkerFileName);
        try
        {
            if (File.Exists(markerPath))
                File.Move(markerPath, abortedPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            logger.LogError(
                exception,
                "Failed to quarantine an unsafe pending deletion marker. MarkerPath={MarkerPath}",
                markerPath);
        }
    }

    private bool CanDeleteStagedDirectory(string stagedDirectory)
    {
        if (!Directory.Exists(stagedDirectory))
            return false;
        if (File.Exists(Path.Combine(stagedDirectory, PendingInstanceDeletionDirectory.AbortedMarkerFileName)))
            return false;
        if (!PendingInstanceDeletionDirectory.TryReadValidMarker(stagedDirectory, out var marker))
            return false;
        if (marker.SchemaVersion != 2)
            return true;
        if (GameInstanceSettingsStore.HasIdentity(stagedDirectory, marker.InstanceId!))
            return true;

        QuarantineMarker(stagedDirectory);
        logger.LogError(
            "Pending deletion directory lost transaction ownership before permanent deletion. ExpectedInstanceId={ExpectedInstanceId} StagedDirectory={StagedDirectory}",
            marker.InstanceId,
            stagedDirectory);
        return false;
    }

    private static bool PathExists(string path) => Directory.Exists(path) || File.Exists(path);

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception or PlatformNotSupportedException;

    private static string GetVersionsDirectory(string minecraftDirectory) =>
        Path.GetFullPath(Path.Combine(minecraftDirectory, "versions"));

    private static void EnsureDirectChild(string parentDirectory, string childDirectory)
    {
        if (!string.Equals(Path.GetDirectoryName(childDirectory), parentDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pending deletion directory must be a direct child of the versions directory.", nameof(childDirectory));
    }
}
