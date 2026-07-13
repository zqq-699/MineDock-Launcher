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
