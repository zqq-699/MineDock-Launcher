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

internal enum PendingInstanceRenameMarkerStatus
{
    Missing,
    Valid,
    Invalid,
    Unreadable
}

internal readonly record struct PendingInstanceRenameMarkerReadResult(
    PendingInstanceRenameMarkerStatus Status,
    PendingInstanceRenameMarker? Marker = null,
    Exception? Exception = null);

internal readonly record struct RenameStagingResult(
    string Directory,
    PendingInstanceRenameMarker Marker);

internal static class PendingInstanceRenameMarkerFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static PendingInstanceRenameMarkerReadResult Read(string directory)
    {
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(directory);
        var presence = ProbePresence(directory);
        if (presence is not null)
            return presence.Value;
        try
        {
            using var stream = OpenRead(markerPath, useAsync: false);
            var marker = JsonSerializer.Deserialize<PendingInstanceRenameMarker>(stream, JsonOptions);
            Validate(directory, marker);
            return new(PendingInstanceRenameMarkerStatus.Valid, marker);
        }
        catch (FileNotFoundException)
        {
            return new(PendingInstanceRenameMarkerStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new(PendingInstanceRenameMarkerStatus.Missing);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new(PendingInstanceRenameMarkerStatus.Invalid, Exception: exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(PendingInstanceRenameMarkerStatus.Unreadable, Exception: exception);
        }
    }

    public static async Task<PendingInstanceRenameMarkerReadResult> ReadAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(directory);
        var presence = ProbePresence(directory);
        if (presence is not null)
            return presence.Value;
        try
        {
            await using var stream = OpenRead(markerPath, useAsync: true);
            var marker = await JsonSerializer.DeserializeAsync<PendingInstanceRenameMarker>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            Validate(directory, marker);
            return new(PendingInstanceRenameMarkerStatus.Valid, marker);
        }
        catch (FileNotFoundException)
        {
            return new(PendingInstanceRenameMarkerStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new(PendingInstanceRenameMarkerStatus.Missing);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new(PendingInstanceRenameMarkerStatus.Invalid, Exception: exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(PendingInstanceRenameMarkerStatus.Unreadable, Exception: exception);
        }
    }

    private static FileStream OpenRead(string markerPath, bool useAsync) => new(
        markerPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read | FileShare.Delete,
        bufferSize: 4096,
        useAsync);

    private static PendingInstanceRenameMarkerReadResult? ProbePresence(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(
                    directory,
                    PendingInstanceRenameDirectory.MarkerFileName,
                    SearchOption.TopDirectoryOnly)
                .Any()
                ? null
                : new(PendingInstanceRenameMarkerStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new(PendingInstanceRenameMarkerStatus.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(PendingInstanceRenameMarkerStatus.Unreadable, Exception: exception);
        }
    }

    private static void Validate(string directory, PendingInstanceRenameMarker? marker)
    {
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

        var directoryName = Path.GetFileName(
            directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (PendingInstanceRenameDirectory.IsPending(directory))
        {
            var expectedName = $"{PendingInstanceRenameDirectory.Prefix}{marker.OldName}-{marker.TransactionId[..8].ToLowerInvariant()}";
            if (!string.Equals(directoryName, expectedName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pending instance rename marker does not match its staging directory.");
            return;
        }

        if (!string.Equals(directoryName, marker.OldName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(directoryName, marker.NewName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Pending instance rename marker does not match its directory.");
        }
    }
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

    public async Task RecoverAllAsync(string minecraftDirectory, CancellationToken cancellationToken)
    {
        var versionsDirectory = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions"));
        if (!Directory.Exists(versionsDirectory))
            return;

        foreach (var directory in EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteAbortedMarker(directory);
            var markerResult = await PendingInstanceRenameMarkerFile.ReadAsync(directory, cancellationToken)
                .ConfigureAwait(false);
            if (markerResult.Status == PendingInstanceRenameMarkerStatus.Missing)
                continue;
            if (markerResult.Status == PendingInstanceRenameMarkerStatus.Invalid)
            {
                HandleInvalidMarker(directory, markerResult.Exception);
                continue;
            }
            if (markerResult.Status == PendingInstanceRenameMarkerStatus.Unreadable)
            {
                logger.LogWarning(
                    markerResult.Exception,
                    "Pending instance rename marker could not be read and remains for a later retry. MarkerPath={MarkerPath}",
                    PendingInstanceRenameDirectory.GetMarkerPath(directory));
                continue;
            }

            var marker = markerResult.Marker!;

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

                var destinationDirectory = GetDestinationDirectory(directory, marker.NewName);
                if (IsPathOccupied(destinationDirectory))
                {
                    await RollbackRenameAsync(directory, marker, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }

                var staging = await MoveSourceToStagingAsync(
                    directory, marker, CancellationToken.None, rollbackMarkerOnFailure: false).ConfigureAwait(false);
                await CompleteCommittedRenameAsync(staging.Directory, staging.Marker, CancellationToken.None)
                    .ConfigureAwait(false);
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
        if (IsPathOccupied(destinationDirectory))
            throw new InstanceInstallNameConflictException(Path.GetFileName(destinationDirectory));
        if (PendingInstanceInstallDirectory.IsLogicalNameReserved(
                Path.GetDirectoryName(destinationDirectory)!,
                Path.GetFileName(destinationDirectory)))
        {
            throw new InstanceInstallNameConflictException(Path.GetFileName(destinationDirectory));
        }
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

    private async Task<RenameStagingResult> MoveSourceToStagingAsync(
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
            if (IsPathOccupied(stagedDirectory))
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
                await MoveWithRetryAsync(sourceDirectory, stagedDirectory, marker.InstanceId, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "Instance rename entered committed staging. OldName={OldName} NewName={NewName} StagedDirectory={StagedDirectory}",
                    marker.OldName,
                    marker.NewName,
                    stagedDirectory);
                return new RenameStagingResult(stagedDirectory, currentMarker);
            }
            catch (Exception) when (!Directory.Exists(sourceDirectory) && Directory.Exists(stagedDirectory))
            {
                return new RenameStagingResult(stagedDirectory, currentMarker);
            }
            catch (IOException) when (Directory.Exists(sourceDirectory) && IsPathOccupied(stagedDirectory))
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
        await EnsureOwnedOrQuarantineAsync(currentDirectory, marker, cancellationToken).ConfigureAwait(false);
        var destinationDirectory = GetDestinationDirectory(currentDirectory, marker.NewName);

        if (PendingInstanceRenameDirectory.IsPending(currentDirectory))
        {
            if (IsPathOccupied(destinationDirectory))
            {
                await RollbackRenameAsync(currentDirectory, marker, CancellationToken.None).ConfigureAwait(false);
                throw new InstanceInstallNameConflictException(marker.NewName);
            }

            await RenameArtifactsAsync(currentDirectory, marker, cancellationToken).ConfigureAwait(false);
            if (IsPathOccupied(destinationDirectory))
            {
                await RollbackRenameAsync(currentDirectory, marker, CancellationToken.None).ConfigureAwait(false);
                throw new InstanceInstallNameConflictException(marker.NewName);
            }

            try
            {
                await MoveOwnedDirectoryAsync(
                        currentDirectory,
                        destinationDirectory,
                        marker.InstanceId,
                        cancellationToken)
                    .ConfigureAwait(false);
                currentDirectory = destinationDirectory;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (!Directory.Exists(currentDirectory) && Directory.Exists(destinationDirectory))
                {
                    currentDirectory = destinationDirectory;
                }
                else if (Directory.Exists(currentDirectory) && IsPathOccupied(destinationDirectory))
                {
                    await RollbackRenameAsync(currentDirectory, marker, CancellationToken.None).ConfigureAwait(false);
                    throw new InstanceInstallNameConflictException(marker.NewName);
                }
                else
                {
                    throw;
                }
            }

            await EnsureOwnedOrQuarantineAsync(currentDirectory, marker, CancellationToken.None)
                .ConfigureAwait(false);
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

    private async Task RollbackRenameAsync(
        string currentDirectory,
        PendingInstanceRenameMarker marker,
        CancellationToken cancellationToken)
    {
        await EnsureOwnedOrQuarantineAsync(currentDirectory, marker, cancellationToken).ConfigureAwait(false);
        await RestoreArtifactsAsync(currentDirectory, marker, cancellationToken).ConfigureAwait(false);

        if (PendingInstanceRenameDirectory.IsPending(currentDirectory))
        {
            var originalDirectory = GetDestinationDirectory(currentDirectory, marker.OldName);
            if (IsPathOccupied(originalDirectory))
                throw new IOException($"Rename rollback destination already exists: {originalDirectory}");

            await EnsureOwnedOrQuarantineAsync(currentDirectory, marker, cancellationToken).ConfigureAwait(false);
            await MoveWithRetryAsync(currentDirectory, originalDirectory, marker.InstanceId, cancellationToken)
                .ConfigureAwait(false);
            currentDirectory = originalDirectory;
        }

        await DeleteOrQuarantineMarkerAsync(currentDirectory, cancellationToken).ConfigureAwait(false);
        logger.LogWarning(
            "Pending instance rename was rolled back because its destination is occupied. InstanceId={InstanceId} OldName={OldName} NewName={NewName}",
            marker.InstanceId,
            marker.OldName,
            marker.NewName);
    }

    private static async Task RestoreArtifactsAsync(
        string directory,
        PendingInstanceRenameMarker marker,
        CancellationToken cancellationToken)
    {
        RenameOptionalArtifact(directory, $"{marker.NewName}.jar", $"{marker.OldName}.jar");
        RenameOptionalDirectory(directory, $"{marker.NewName}-natives", $"{marker.OldName}-natives");

        var newJsonPath = Path.Combine(directory, $"{marker.NewName}.json");
        var oldJsonPath = Path.Combine(directory, $"{marker.OldName}.json");
        if (File.Exists(newJsonPath) && File.Exists(oldJsonPath))
            throw new IOException("Both old and new version JSON files exist while rolling back rename.");
        if (File.Exists(newJsonPath))
            File.Move(newJsonPath, oldJsonPath);
        if (!File.Exists(oldJsonPath))
            throw new FileNotFoundException("Version JSON was not found while rolling back rename.", oldJsonPath);

        JsonObject json;
        await using (var stream = File.OpenRead(oldJsonPath))
        {
            json = (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Version JSON is empty.")).AsObject();
        }
        json["id"] = marker.OldName;
        if (json["jar"] is JsonValue jarValue
            && string.Equals(jarValue.ToString(), marker.NewName, StringComparison.OrdinalIgnoreCase))
        {
            json["jar"] = marker.OldName;
        }
        await AtomicJsonFileWriter.WriteAsync(oldJsonPath, json, JsonOptions, cancellationToken).ConfigureAwait(false);
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

    private async Task MoveWithRetryAsync(
        string source,
        string destination,
        string expectedInstanceId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await MoveOwnedDirectoryAsync(source, destination, expectedInstanceId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (
                attempt < MaxMoveAttempts
                && exception is IOException or UnauthorizedAccessException
                && !IsPathOccupied(destination))
            {
                logger.LogWarning(exception, "Version directory move will be retried. Attempt={Attempt} MaxAttempts={MaxAttempts}", attempt, MaxMoveAttempts);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task MoveOwnedDirectoryAsync(
        string source,
        string destination,
        string expectedInstanceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!useIdentitySafeMove)
        {
            await moveDirectoryAsync(source, destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            WindowsDirectoryHandleMover.MoveOwnedDirectory(
                source,
                destination,
                () => GameInstanceSettingsStore.HasIdentity(source, expectedInstanceId),
                beforeOwnedDirectoryMove is null
                    ? null
                    : () => beforeOwnedDirectoryMove(source, destination));
        }
        catch (InvalidOperationException)
        {
            throw new GameInstanceMutationConflictException(
                expectedInstanceId,
                Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
        }
    }

    private static Task MoveDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(source, destination);
        return Task.CompletedTask;
    }

    private string GetDestinationDirectory(string currentDirectory, string versionName)
    {
        return directoryManager.GetVersionDirectory(
            Path.GetDirectoryName(Path.GetDirectoryName(currentDirectory)!)!,
            versionName);
    }

    private static bool IsPathOccupied(string path) => Directory.Exists(path) || File.Exists(path);

    private async Task DeleteOrQuarantineMarkerAsync(string directory, CancellationToken cancellationToken)
    {
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(directory);
        try
        {
            await DeleteMarkerWithRetryAsync(markerPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception deleteException)
        {
            var abortedMarkerPath = Path.Combine(directory, PendingInstanceRenameDirectory.AbortedMarkerFileName);
            try
            {
                quarantineMarker(markerPath, abortedMarkerPath);
                logger.LogError(
                    deleteException,
                    "Rolled-back rename marker could not be deleted and was quarantined. MarkerPath={MarkerPath} AbortedMarkerPath={AbortedMarkerPath}",
                    markerPath,
                    abortedMarkerPath);
            }
            catch (Exception quarantineException)
            {
                throw new AggregateException(
                    "Rolled-back rename marker could not be deleted or quarantined.",
                    deleteException,
                    quarantineException);
            }
        }
    }

    private async Task EnsureOwnedOrQuarantineAsync(
        string directory,
        PendingInstanceRenameMarker marker,
        CancellationToken cancellationToken)
    {
        try
        {
            var markerResult = await PendingInstanceRenameMarkerFile.ReadAsync(directory, cancellationToken)
                .ConfigureAwait(false);
            if (markerResult.Status != PendingInstanceRenameMarkerStatus.Valid
                || markerResult.Marker is null
                || !string.Equals(
                    markerResult.Marker.TransactionId,
                    marker.TransactionId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(markerResult.Marker.InstanceId, marker.InstanceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(markerResult.Marker.OldName, marker.OldName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(markerResult.Marker.NewName, marker.NewName, StringComparison.OrdinalIgnoreCase))
            {
                throw new GameInstanceMutationConflictException(marker.InstanceId, marker.OldName);
            }

            await instanceSettingsStore.EnsureIdentityAsync(directory, marker.InstanceId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GameInstanceMutationConflictException)
        {
            QuarantineUnsafeMarker(directory, marker);
            throw;
        }
    }

    private void QuarantineUnsafeMarker(string directory, PendingInstanceRenameMarker marker)
    {
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(directory);
        var abortedMarkerPath = Path.Combine(directory, PendingInstanceRenameDirectory.AbortedMarkerFileName);
        try
        {
            if (File.Exists(markerPath))
                quarantineMarker(markerPath, abortedMarkerPath);
            logger.LogError(
                "Pending rename marker was quarantined because the directory belongs to another instance. ExpectedInstanceId={ExpectedInstanceId} Directory={Directory}",
                marker.InstanceId,
                directory);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unsafe pending rename marker could not be quarantined. ExpectedInstanceId={ExpectedInstanceId} Directory={Directory}",
                marker.InstanceId,
                directory);
        }
    }

    private void HandleInvalidMarker(string directory, Exception? validationException)
    {
        var markerPath = PendingInstanceRenameDirectory.GetMarkerPath(directory);
        if (PendingInstanceRenameDirectory.IsPending(directory))
        {
            logger.LogError(
                validationException,
                "Invalid pending instance rename marker was preserved in a staging directory. MarkerPath={MarkerPath}",
                markerPath);
            return;
        }

        var abortedMarkerPath = Path.Combine(directory, PendingInstanceRenameDirectory.AbortedMarkerFileName);
        try
        {
            quarantineMarker(markerPath, abortedMarkerPath);
            logger.LogError(
                validationException,
                "Invalid pending instance rename marker was quarantined. MarkerPath={MarkerPath} AbortedMarkerPath={AbortedMarkerPath}",
                markerPath,
                abortedMarkerPath);
        }
        catch (Exception quarantineException)
        {
            logger.LogError(
                quarantineException,
                "Invalid pending instance rename marker could not be quarantined but will not hide the instance. MarkerPath={MarkerPath}",
                markerPath);
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
