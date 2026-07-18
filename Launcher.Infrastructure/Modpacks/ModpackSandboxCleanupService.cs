/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ModpackSandboxCleanupService : IModpackSandboxCleanupService
{
    internal const string MarkerFileName = ".bhl-modpack-sandbox.json";
    private const string LockDirectoryName = ".locks";
    private const int MarkerSchemaVersion = 1;
    private readonly object tasksLock = new();
    private readonly HashSet<Task> pendingTasks = [];
    private readonly string tempRootDirectory;
    private readonly Action<string> deleteTree;
    private readonly ILogger logger;

    public ModpackSandboxCleanupService(ILogger<ModpackSandboxCleanupService>? logger = null)
        : this(Path.GetTempPath(), deleteTree: null, logger)
    {
    }

    internal ModpackSandboxCleanupService(
        string tempRootDirectory,
        Action<string>? deleteTree = null,
        ILogger? logger = null)
    {
        this.tempRootDirectory = Path.GetFullPath(tempRootDirectory);
        this.deleteTree = deleteTree ?? DeleteTree;
        this.logger = logger ?? NullLogger.Instance;
    }

    public IModpackSandboxSession CreateSession(ModpackSandboxKind kind)
    {
        var rootDirectory = GetKindRoot(kind);
        var lockDirectory = Path.Combine(rootDirectory, LockDirectoryName);
        Directory.CreateDirectory(lockDirectory);

        var transactionId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(rootDirectory, transactionId);
        var lockPath = Path.Combine(lockDirectory, $"{transactionId}.lock");
        var recoveryMarkerPath = GetRecoveryMarkerPath(kind, transactionId);
        FileStream? activeLock = null;
        try
        {
            activeLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var marker = new SandboxMarker(
                MarkerSchemaVersion,
                transactionId,
                kind.ToString(),
                DateTimeOffset.UtcNow);
            WriteMarker(recoveryMarkerPath, marker);
            Directory.CreateDirectory(directory);
            WriteMarker(Path.Combine(directory, MarkerFileName), marker);
            logger.LogDebug(
                "Created modpack loader sandbox. Kind={Kind} TransactionId={TransactionId} Directory={Directory}",
                kind,
                transactionId,
                directory);
            return new SandboxSession(this, kind, transactionId, directory, activeLock);
        }
        catch
        {
            activeLock?.Dispose();
            TryDeleteSessionDirectory(directory, kind, transactionId);
            throw;
        }
    }

    public Task CleanupStaleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Track(Task.Run(() => CleanupStaleCore(cancellationToken), CancellationToken.None));
    }

    public async Task WaitForPendingCleanupAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task[] snapshot;
            lock (tasksLock)
                snapshot = pendingTasks.ToArray();
            if (snapshot.Length == 0)
                return;
            await Task.WhenAll(snapshot).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task CompleteSessionAsync(SandboxSession session, bool deferCleanup)
    {
        if (!session.TryReleaseLock())
            return Task.CompletedTask;

        if (deferCleanup)
        {
            logger.LogDebug(
                "Deferring modpack loader sandbox cleanup after cancellation. Directory={Directory}",
                session.DirectoryPath);
            _ = Track(Task.Run(
                () => TryDeleteSessionDirectory(session.DirectoryPath, session.Kind, session.TransactionId),
                CancellationToken.None));
            return Task.CompletedTask;
        }

        TryDeleteSessionDirectory(session.DirectoryPath, session.Kind, session.TransactionId);
        return Task.CompletedTask;
    }

    private Task Track(Task task)
    {
        lock (tasksLock)
            pendingTasks.Add(task);
        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
                lock (tasksLock)
                    pendingTasks.Remove(completedTask);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private void CleanupStaleCore(CancellationToken cancellationToken)
    {
        foreach (var kind in Enum.GetValues<ModpackSandboxKind>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootDirectory = GetKindRoot(kind);
            if (!Directory.Exists(rootDirectory))
                continue;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(rootDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Failed to enumerate modpack loader sandbox root. Directory={Directory}", rootDirectory);
                continue;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFileName(directory), LockDirectoryName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryReadValidMarker(directory, kind, out var marker)
                    && !TryReadValidRecoveryMarker(directory, kind, out marker))
                {
                    logger.LogWarning(
                        "Modpack loader sandbox was preserved because its marker is missing or invalid. Directory={Directory}",
                        directory);
                    continue;
                }

                var lockPath = GetLockPath(kind, marker.TransactionId);
                FileStream? activeLock;
                try
                {
                    activeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException exception) when (IsSharingViolation(exception))
                {
                    logger.LogDebug("Skipping active modpack loader sandbox. Directory={Directory}", directory);
                    continue;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Failed to acquire modpack loader sandbox cleanup lock. Directory={Directory}", directory);
                    continue;
                }

                var deleted = false;
                using (activeLock)
                {
                    if (!TryReadValidMarker(directory, kind, out var confirmedMarker)
                        && !TryReadValidRecoveryMarker(directory, kind, out confirmedMarker))
                    {
                        logger.LogWarning(
                            "Modpack loader sandbox changed while waiting for its cleanup lock and was preserved. Directory={Directory}",
                            directory);
                        continue;
                    }
                    deleted = TryDeleteSessionDirectory(
                        directory,
                        kind,
                        confirmedMarker.TransactionId,
                        deleteLock: false);
                }
                if (deleted)
                {
                    TryDeleteFile(lockPath);
                    TryDeleteFile(GetRecoveryMarkerPath(kind, marker.TransactionId));
                }
            }

            CleanupOrphanedRecoveryMarkers(kind, cancellationToken);
        }
    }

    private bool TryDeleteSessionDirectory(
        string directory,
        ModpackSandboxKind kind,
        string transactionId,
        bool deleteLock = true)
    {
        try
        {
            var presence = GetDirectoryPresence(directory);
            if (presence == DirectoryPresence.Unknown)
                return false;
            if (presence == DirectoryPresence.Present)
                deleteTree(directory);
            var deleted = GetDirectoryPresence(directory) == DirectoryPresence.Absent;
            if (deleteLock && deleted)
            {
                TryDeleteFile(GetLockPath(kind, transactionId));
                TryDeleteFile(GetRecoveryMarkerPath(kind, transactionId));
            }
            if (deleted)
            {
                logger.LogDebug(
                    "Cleaned modpack loader sandbox. Kind={Kind} TransactionId={TransactionId} Directory={Directory}",
                    kind,
                    transactionId,
                    directory);
            }
            return deleted;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Failed to delete modpack loader sandbox directory. Directory={Directory}", directory);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unexpected failure while deleting modpack loader sandbox directory. Directory={Directory}", directory);
            return false;
        }
    }

    private bool TryReadValidMarker(string directory, ModpackSandboxKind kind, out SandboxMarker marker)
    {
        marker = default!;
        try
        {
            var normalized = Path.GetFullPath(directory);
            var rootDirectory = GetKindRoot(kind);
            if (!string.Equals(Path.GetDirectoryName(normalized), rootDirectory, StringComparison.OrdinalIgnoreCase))
                return false;
            var transactionId = Path.GetFileName(normalized);
            if (!Guid.TryParseExact(transactionId, "N", out _))
                return false;
            if (!TryReadMarker(Path.Combine(normalized, MarkerFileName), out var parsed)
                || !IsValidMarker(parsed, transactionId, kind))
                return false;
            marker = parsed;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private bool TryReadValidRecoveryMarker(
        string directory,
        ModpackSandboxKind kind,
        out SandboxMarker marker)
    {
        marker = default!;
        var normalized = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(normalized), GetKindRoot(kind), StringComparison.OrdinalIgnoreCase))
            return false;
        var transactionId = Path.GetFileName(normalized);
        if (!Guid.TryParseExact(transactionId, "N", out _)
            || !TryReadMarker(GetRecoveryMarkerPath(kind, transactionId), out var parsed)
            || !IsValidMarker(parsed, transactionId, kind))
            return false;
        marker = parsed;
        return true;
    }

    private static bool TryReadMarker(string path, out SandboxMarker marker)
    {
        marker = default!;
        try
        {
            var parsed = JsonSerializer.Deserialize<SandboxMarker>(File.ReadAllText(path));
            if (parsed is null)
                return false;
            marker = parsed;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool IsValidMarker(
        SandboxMarker marker,
        string transactionId,
        ModpackSandboxKind kind) =>
        marker.SchemaVersion == MarkerSchemaVersion
        && marker.CreatedAtUtc != default
        && string.Equals(marker.TransactionId, transactionId, StringComparison.Ordinal)
        && string.Equals(marker.Kind, kind.ToString(), StringComparison.Ordinal);

    private void CleanupOrphanedRecoveryMarkers(
        ModpackSandboxKind kind,
        CancellationToken cancellationToken)
    {
        var lockDirectory = Path.Combine(GetKindRoot(kind), LockDirectoryName);
        if (!Directory.Exists(lockDirectory))
            return;
        foreach (var markerPath in Directory.EnumerateFiles(lockDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionId = Path.GetFileNameWithoutExtension(markerPath);
            if (!Guid.TryParseExact(transactionId, "N", out _)
                || !TryReadMarker(markerPath, out var marker)
                || !IsValidMarker(marker, transactionId, kind)
                || GetDirectoryPresence(Path.Combine(GetKindRoot(kind), transactionId)) != DirectoryPresence.Absent)
                continue;
            var lockPath = GetLockPath(kind, transactionId);
            FileStream? activeLock;
            try
            {
                activeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(exception, "Failed to inspect orphaned modpack sandbox recovery marker. Path={Path}", markerPath);
                continue;
            }
            using (activeLock)
                TryDeleteFile(markerPath);
            TryDeleteFile(lockPath);
        }
    }

    private static void WriteMarker(string markerPath, SandboxMarker marker)
    {
        using var stream = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, marker);
        stream.Flush(flushToDisk: true);
    }

    private string GetKindRoot(ModpackSandboxKind kind) => Path.Combine(
        tempRootDirectory,
        kind switch
        {
            ModpackSandboxKind.ModpackVersion => "launcher-modpack-version",
            ModpackSandboxKind.InstanceVersion => "launcher-instance-version",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        });

    private string GetLockPath(ModpackSandboxKind kind, string transactionId) =>
        Path.Combine(GetKindRoot(kind), LockDirectoryName, $"{transactionId}.lock");

    private string GetRecoveryMarkerPath(ModpackSandboxKind kind, string transactionId) =>
        Path.Combine(GetKindRoot(kind), LockDirectoryName, $"{transactionId}.json");

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Failed to remove modpack loader sandbox lock file. Path={Path}", path);
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static void DeleteTree(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, recursive: false);
            return;
        }
        foreach (var child in Directory.EnumerateDirectories(path))
            DeleteTree(child);
        foreach (var file in Directory.EnumerateFiles(path))
            File.Delete(file);
        Directory.Delete(path, recursive: false);
    }

    private static DirectoryPresence GetDirectoryPresence(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) != 0
                ? DirectoryPresence.Present
                : DirectoryPresence.Unknown;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return DirectoryPresence.Absent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DirectoryPresence.Unknown;
        }
    }

    private enum DirectoryPresence
    {
        Absent,
        Present,
        Unknown
    }

    private sealed record SandboxMarker(
        int SchemaVersion,
        string TransactionId,
        string Kind,
        DateTimeOffset CreatedAtUtc);

    private sealed class SandboxSession(
        ModpackSandboxCleanupService owner,
        ModpackSandboxKind kind,
        string transactionId,
        string directoryPath,
        FileStream activeLock) : IModpackSandboxSession
    {
        private FileStream? activeLock = activeLock;

        public ModpackSandboxKind Kind { get; } = kind;
        public string TransactionId { get; } = transactionId;
        public string DirectoryPath { get; } = directoryPath;

        public Task CleanupAsync(bool deferCleanup) => owner.CompleteSessionAsync(this, deferCleanup);

        public ValueTask DisposeAsync()
        {
            if (TryReleaseLock())
                owner.logger.LogWarning(
                    "Modpack loader sandbox session was disposed without cleanup and will be recovered later. Directory={Directory}",
                    DirectoryPath);
            return ValueTask.CompletedTask;
        }

        public bool TryReleaseLock()
        {
            var stream = Interlocked.Exchange(ref activeLock, null);
            if (stream is null)
                return false;
            stream.Dispose();
            return true;
        }
    }
}
