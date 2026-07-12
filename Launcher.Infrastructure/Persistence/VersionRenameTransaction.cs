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
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Persistence;

internal static class PendingInstanceRenameDirectory
{
    public const string Prefix = ".bhl-rename-pending-";
    public const string MarkerFileName = ".bhl-rename-pending.json";
    public const string AbortedMarkerFileName = ".bhl-rename-aborted.json";

    public static bool IsPending(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string GetMarkerPath(string directory) => Path.Combine(directory, MarkerFileName);
}

internal sealed record PendingInstanceRenameMarker
{
    public int SchemaVersion { get; init; } = 1;
    public string TransactionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string OldName { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string? NewIconSource { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

internal sealed class VersionRenameTransaction
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
    private readonly Func<Guid> guidFactory;
    private readonly Action<string> deleteMarker;
    private readonly ILogger logger;

    public VersionRenameTransaction(
        VersionDirectoryManager directoryManager,
        GameInstanceSettingsStore instanceSettingsStore,
        ILogger logger,
        Func<string, string, CancellationToken, Task>? moveDirectoryAsync = null,
        Func<Guid>? guidFactory = null,
        Action<string>? deleteMarker = null)
    {
        this.directoryManager = directoryManager;
        this.instanceSettingsStore = instanceSettingsStore;
        this.logger = logger;
        this.moveDirectoryAsync = moveDirectoryAsync ?? MoveDirectoryAsync;
        this.guidFactory = guidFactory ?? Guid.NewGuid;
        this.deleteMarker = deleteMarker ?? File.Delete;
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

        var marker = CreateMarker(instance.Id, oldVersionName, newVersionName, newIconSource, updatedAt);
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(sourceDirectory);
        await AtomicJsonFileWriter.WriteAsync(markerPath, marker, JsonOptions, cancellationToken).ConfigureAwait(false);

        string stagedDirectory;
        try
        {
            stagedDirectory = await MoveSourceToStagingAsync(
                sourceDirectory, marker, cancellationToken, rollbackMarkerOnFailure: true).ConfigureAwait(false);
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

    public async Task RecoverAllAsync(string minecraftDirectory, CancellationToken cancellationToken)
    {
        var versionsDirectory = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions"));
        if (!Directory.Exists(versionsDirectory))
            return;

        foreach (var directory in EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteAbortedMarker(directory);
            var marker = await TryReadMarkerAsync(directory, cancellationToken).ConfigureAwait(false);
            if (marker is null)
                continue;

            try
            {
                if (PendingInstanceRenameDirectory.IsPending(directory))
                {
                    await CompleteCommittedRenameAsync(directory, marker, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }

                var directoryName = Path.GetFileName(directory);
                if (string.Equals(directoryName, marker.NewName, StringComparison.OrdinalIgnoreCase))
                {
                    await CompleteCommittedRenameAsync(directory, marker, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(directoryName, marker.OldName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Pending rename marker does not match its directory.");

                var stagedDirectory = await MoveSourceToStagingAsync(
                    directory, marker, CancellationToken.None, rollbackMarkerOnFailure: false).ConfigureAwait(false);
                await CompleteCommittedRenameAsync(stagedDirectory, marker, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Pending instance rename remains for a later retry. Directory={Directory} OldName={OldName} NewName={NewName}",
                    directory,
                    marker.OldName,
                    marker.NewName);
            }
        }
    }

    private static void ValidateNewTransaction(string sourceDirectory, string destinationDirectory, string oldVersionName)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory not found: {sourceDirectory}");
        if (!File.Exists(Path.Combine(sourceDirectory, $"{oldVersionName}.json")))
            throw new FileNotFoundException("Version JSON not found.", Path.Combine(sourceDirectory, $"{oldVersionName}.json"));
        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {destinationDirectory}");
        if (File.Exists(PendingInstanceRenameDirectory.GetMarkerPath(sourceDirectory)))
            throw new IOException($"Version directory already has a pending rename marker: {sourceDirectory}");
    }

    private PendingInstanceRenameMarker CreateMarker(
        string instanceId,
        string oldName,
        string newName,
        string? newIconSource,
        DateTimeOffset updatedAt) => new()
    {
        TransactionId = guidFactory().ToString("N"),
        InstanceId = instanceId,
        OldName = oldName,
        NewName = newName,
        NewIconSource = newIconSource,
        UpdatedAtUtc = updatedAt
    };

    private async Task<string> MoveSourceToStagingAsync(
        string sourceDirectory,
        PendingInstanceRenameMarker marker,
        CancellationToken cancellationToken,
        bool rollbackMarkerOnFailure)
    {
        var currentMarker = marker;
        for (var targetAttempt = 1; targetAttempt <= MaxStagingTargets; targetAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(sourceDirectory)!;
            var suffix = currentMarker.TransactionId[..8].ToLowerInvariant();
            var stagedDirectory = Path.Combine(parent, $"{PendingInstanceRenameDirectory.Prefix}{marker.OldName}-{suffix}");
            if (Directory.Exists(stagedDirectory))
            {
                if (targetAttempt == MaxStagingTargets)
                    break;
                currentMarker = marker with { TransactionId = guidFactory().ToString("N") };
                await AtomicJsonFileWriter.WriteAsync(
                    PendingInstanceRenameDirectory.GetMarkerPath(sourceDirectory),
                    currentMarker,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await MoveWithRetryAsync(sourceDirectory, stagedDirectory, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Instance rename entered committed staging. OldName={OldName} NewName={NewName} StagedDirectory={StagedDirectory}",
                    marker.OldName,
                    marker.NewName,
                    stagedDirectory);
                return stagedDirectory;
            }
            catch (Exception) when (!Directory.Exists(sourceDirectory) && Directory.Exists(stagedDirectory))
            {
                return stagedDirectory;
            }
            catch (IOException) when (Directory.Exists(sourceDirectory) && Directory.Exists(stagedDirectory))
            {
                if (targetAttempt == MaxStagingTargets)
                    break;
                currentMarker = marker with { TransactionId = guidFactory().ToString("N") };
                await AtomicJsonFileWriter.WriteAsync(
                    PendingInstanceRenameDirectory.GetMarkerPath(sourceDirectory),
                    currentMarker,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (!rollbackMarkerOnFailure)
                    throw;
                throw;
            }
        }

        throw new IOException($"Unable to allocate a pending rename directory after {MaxStagingTargets} attempts.");
    }

    private async Task CompleteCommittedRenameAsync(
        string currentDirectory,
        PendingInstanceRenameMarker marker,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = directoryManager.GetVersionDirectory(
            Path.GetDirectoryName(Path.GetDirectoryName(currentDirectory)!)!,
            marker.NewName);

        if (PendingInstanceRenameDirectory.IsPending(currentDirectory))
        {
            await RenameArtifactsAsync(currentDirectory, marker, cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(destinationDirectory))
                throw new IOException($"Rename destination already exists: {destinationDirectory}");
            await moveDirectoryAsync(currentDirectory, destinationDirectory, cancellationToken).ConfigureAwait(false);
            currentDirectory = destinationDirectory;
        }

        await instanceSettingsStore.CompleteRenameAsync(
            currentDirectory,
            marker.InstanceId,
            marker.NewName,
            marker.NewIconSource,
            marker.UpdatedAtUtc,
            cancellationToken).ConfigureAwait(false);
        await DeleteMarkerWithRetryAsync(
            PendingInstanceRenameDirectory.GetMarkerPath(currentDirectory),
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Pending instance rename completed. InstanceId={InstanceId} OldName={OldName} NewName={NewName}",
            marker.InstanceId,
            marker.OldName,
            marker.NewName);
    }

    private static async Task RenameArtifactsAsync(
        string directory,
        PendingInstanceRenameMarker marker,
        CancellationToken cancellationToken)
    {
        RenameOptionalArtifact(directory, $"{marker.OldName}.jar", $"{marker.NewName}.jar");
        RenameOptionalDirectory(directory, $"{marker.OldName}-natives", $"{marker.NewName}-natives");

        var oldJsonPath = Path.Combine(directory, $"{marker.OldName}.json");
        var newJsonPath = Path.Combine(directory, $"{marker.NewName}.json");
        if (File.Exists(oldJsonPath) && File.Exists(newJsonPath))
            throw new IOException("Both old and new version JSON files exist.");
        if (File.Exists(oldJsonPath))
            File.Move(oldJsonPath, newJsonPath);
        if (!File.Exists(newJsonPath))
            throw new FileNotFoundException("Version JSON was not found while resuming rename.", newJsonPath);

        JsonObject json;
        await using (var stream = File.OpenRead(newJsonPath))
        {
            json = (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Version JSON is empty.")).AsObject();
        }
        json["id"] = marker.NewName;
        if (json["jar"] is JsonValue jarValue
            && string.Equals(jarValue.ToString(), marker.OldName, StringComparison.OrdinalIgnoreCase))
        {
            json["jar"] = marker.NewName;
        }
        await AtomicJsonFileWriter.WriteAsync(newJsonPath, json, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void RenameOptionalArtifact(string directory, string oldName, string newName)
    {
        var oldPath = Path.Combine(directory, oldName);
        var newPath = Path.Combine(directory, newName);
        if (File.Exists(oldPath) && File.Exists(newPath))
            throw new IOException($"Both old and new rename artifacts exist: {oldName}, {newName}");
        if (File.Exists(oldPath))
            File.Move(oldPath, newPath);
    }

    private static void RenameOptionalDirectory(string directory, string oldName, string newName)
    {
        var oldPath = Path.Combine(directory, oldName);
        var newPath = Path.Combine(directory, newName);
        if (Directory.Exists(oldPath) && Directory.Exists(newPath))
            throw new IOException($"Both old and new rename directories exist: {oldName}, {newName}");
        if (Directory.Exists(oldPath))
            Directory.Move(oldPath, newPath);
    }

    private async Task MoveWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await moveDirectoryAsync(source, destination, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (
                attempt < MaxMoveAttempts
                && exception is IOException or UnauthorizedAccessException
                && !Directory.Exists(destination))
            {
                logger.LogWarning(exception, "Version directory move will be retried. Attempt={Attempt} MaxAttempts={MaxAttempts}", attempt, MaxMoveAttempts);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task MoveDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(source, destination);
        return Task.CompletedTask;
    }

    private async Task<PendingInstanceRenameMarker?> TryReadMarkerAsync(string directory, CancellationToken cancellationToken)
    {
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(directory);
        if (!File.Exists(markerPath))
            return null;
        try
        {
            await using var stream = File.OpenRead(markerPath);
            var marker = await JsonSerializer.DeserializeAsync<PendingInstanceRenameMarker>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (marker is null
                || marker.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(marker.TransactionId)
                || marker.TransactionId.Length < 8
                || string.IsNullOrWhiteSpace(marker.InstanceId)
                || !VersionDirectoryName.IsSafeDirectoryName(marker.OldName)
                || !VersionDirectoryName.IsSafeDirectoryName(marker.NewName))
            {
                throw new InvalidDataException("Pending instance rename marker is invalid.");
            }
            return marker;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(exception, "Invalid pending instance rename marker was ignored. MarkerPath={MarkerPath}", markerPath);
            return null;
        }
    }

    private async Task DeleteMarkerWithRetryAsync(string markerPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxMarkerDeleteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(markerPath))
                    deleteMarker(markerPath);
                if (!File.Exists(markerPath))
                    return;
            }
            catch (Exception) when (attempt < MaxMarkerDeleteAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException($"Failed to delete pending rename marker: {markerPath}");
    }

    private void TryDeleteAbortedMarker(string directory)
    {
        var path = Path.Combine(directory, PendingInstanceRenameDirectory.AbortedMarkerFileName);
        try
        {
            if (File.Exists(path))
                deleteMarker(path);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to clean an aborted instance rename marker. MarkerPath={MarkerPath}", path);
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
