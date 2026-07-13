/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Infrastructure.FileSystem;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Persistence;

internal sealed partial class VersionRenameTransaction
{
    private const int MaxMoveAttempts = 5;
    private const int MaxStagingTargets = 3;
    private const int MaxMarkerDeleteAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly VersionDirectoryManager directoryManager;
    private readonly GameInstanceSettingsStore instanceSettingsStore;
    private readonly Func<string, string, CancellationToken, Task> moveDirectoryAsync;
    private readonly bool useIdentitySafeMove;
    private readonly Action<string, string>? beforeOwnedDirectoryMove;
    private readonly Func<Guid> guidFactory;
    private readonly Action<string> deleteMarker;
    private readonly Action<string, string> quarantineMarker;
    private readonly ILogger logger;

    public VersionRenameTransaction(
        VersionDirectoryManager directoryManager,
        GameInstanceSettingsStore instanceSettingsStore,
        ILogger logger,
        Func<string, string, CancellationToken, Task>? moveDirectoryAsync = null,
        Func<Guid>? guidFactory = null,
        Action<string>? deleteMarker = null,
        Action<string, string>? quarantineMarker = null,
        Action<string, string>? beforeOwnedDirectoryMove = null)
    {
        this.directoryManager = directoryManager;
        this.instanceSettingsStore = instanceSettingsStore;
        this.logger = logger;
        this.moveDirectoryAsync = moveDirectoryAsync ?? MoveDirectoryAsync;
        useIdentitySafeMove = moveDirectoryAsync is null;
        this.beforeOwnedDirectoryMove = beforeOwnedDirectoryMove;
        this.guidFactory = guidFactory ?? Guid.NewGuid;
        this.deleteMarker = deleteMarker ?? File.Delete;
        this.quarantineMarker = quarantineMarker ?? ((source, destination) => File.Move(source, destination, overwrite: true));
    }

    public async Task ExecuteAsync(
        string minecraftDirectory,
        GameInstance instance,
        string newVersionName,
        string? newIconSource,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var oldVersionName = string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
        if (string.Equals(oldVersionName, newVersionName, StringComparison.OrdinalIgnoreCase))
            return;

        var sourceDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, oldVersionName);
        var destinationDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, newVersionName);
        ValidateNewTransaction(sourceDirectory, destinationDirectory, oldVersionName);
        await instanceSettingsStore.EnsureIdentityAsync(sourceDirectory, instance.Id, cancellationToken)
            .ConfigureAwait(false);

        var marker = CreateMarker(instance.Id, oldVersionName, newVersionName, newIconSource, updatedAt);
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(sourceDirectory);
        await AtomicJsonFileWriter.WriteAsync(markerPath, marker, JsonOptions, cancellationToken).ConfigureAwait(false);

        string stagedDirectory;
        try
        {
            var staging = await MoveSourceToStagingAsync(
                sourceDirectory, marker, cancellationToken, rollbackMarkerOnFailure: true).ConfigureAwait(false);
            stagedDirectory = staging.Directory;
            marker = staging.Marker;
        }
        catch (Exception moveException)
        {
            try
            {
                await DeleteMarkerWithRetryAsync(markerPath, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                var abortedMarkerPath = Path.Combine(sourceDirectory, PendingInstanceRenameDirectory.AbortedMarkerFileName);
                try
                {
                    File.Move(markerPath, abortedMarkerPath, overwrite: true);
                    logger.LogError(
                        cleanupException,
                        "Pending rename marker could not be deleted and was quarantined after staging failed. MarkerPath={MarkerPath}",
                        markerPath);
                }
                catch (Exception quarantineException)
                {
                    throw new AggregateException(
                        "Instance rename staging and marker rollback both failed.",
                        moveException,
                        cleanupException,
                        quarantineException);
                }
            }
            throw;
        }

        await CompleteCommittedRenameAsync(stagedDirectory, marker, CancellationToken.None).ConfigureAwait(false);
    }
}
