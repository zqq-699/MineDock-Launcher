/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Per launcher/install operation state. It deliberately never outlives the
/// launcher instance and does not classify downloads from their URLs.
/// </summary>
internal sealed class MinecraftDownloadOperationContext : IDisposable
{
    private readonly ConcurrentDictionary<string, AssetIdentity> assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> verifiedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> assetLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly DownloadWorkspaceLease workspace;

    public MinecraftDownloadOperationContext(string managedRoot)
    {
        ManagedRoot = Path.GetFullPath(managedRoot);
        workspace = DownloadWorkspace.CreateOperation(ManagedRoot);
    }

    public string ManagedRoot { get; }
    public string WorkspaceDirectory => workspace.Directory;

    public void Dispose() => workspace.Dispose();

    public void RegisterAsset(string destinationPath, string sha1, long? size)
    {
        var normalized = Path.GetFullPath(destinationPath);
        var identity = new AssetIdentity(sha1, size);
        if (assets.TryGetValue(normalized, out var existing) && existing != identity)
            throw new InvalidDataException("Conflicting asset identities resolved to the same destination path.");
        assets[normalized] = identity;
    }

    public bool TryGetAsset(string destinationPath, string? sha1, long? size)
    {
        var normalized = Path.GetFullPath(destinationPath);
        return assets.TryGetValue(normalized, out var expected)
            && string.Equals(expected.Sha1, sha1, StringComparison.OrdinalIgnoreCase)
            && expected.Size == size;
    }

    public bool IsVerified(string destinationPath, DownloadIntegrityExpectation integrity) =>
        verifiedAssets.ContainsKey(CreateVerificationKey(destinationPath, integrity));

    public void MarkVerified(string destinationPath, DownloadIntegrityExpectation integrity) =>
        verifiedAssets.TryAdd(CreateVerificationKey(destinationPath, integrity), 0);

    public async ValueTask<IDisposable> AcquireAssetLockAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var normalized = Path.GetFullPath(destinationPath);
        var semaphore = assetLocks.GetOrAdd(normalized, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLease(semaphore);
    }

    private static string CreateVerificationKey(string destinationPath, DownloadIntegrityExpectation integrity) =>
        $"{Path.GetFullPath(destinationPath)}|{integrity.ExpectedSize}|{integrity.Fingerprint}";

    private sealed record AssetIdentity(string Sha1, long? Size);

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref semaphore, null)?.Release();
    }
}

internal static class DownloadWorkspace
{
    private const string DirectoryName = ".bhl-download-work";
    private const string OwnerLockFileName = ".owner.lock";
    private static readonly ConcurrentDictionary<string, object> CreationGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> CleanedRoots = new(StringComparer.OrdinalIgnoreCase);

    public static DownloadWorkspaceLease CreateOperation(string managedRoot)
    {
        var root = Path.Combine(Path.GetFullPath(managedRoot), DirectoryName);
        var creationGate = CreationGates.GetOrAdd(root, static _ => new object());
        lock (creationGate)
        {
            Directory.CreateDirectory(root);
            if (CleanedRoots.TryAdd(root, 0))
                CleanupAbandonedOperations(root);

            Exception? lastFailure = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var operation = Path.Combine(root, Guid.NewGuid().ToString("N"));
                if (Directory.Exists(operation))
                    continue;

                Directory.CreateDirectory(operation);
                try
                {
                    return new DownloadWorkspaceLease(operation, OpenOwnerLock(operation));
                }
                catch (IOException exception)
                {
                    TryDeleteOperationDirectory(operation);
                    lastFailure = exception;
                }
                catch (UnauthorizedAccessException exception)
                {
                    TryDeleteOperationDirectory(operation);
                    lastFailure = exception;
                }
            }

            throw new IOException("Failed to create a task-scoped download workspace.", lastFailure);
        }
    }

    public static DownloadWorkspaceLease CreateFallbackOperation(string destinationPath) =>
        CreateOperation(Path.GetDirectoryName(Path.GetFullPath(destinationPath))
            ?? throw new InvalidOperationException("Download destination has no parent directory."));

    private static void CleanupAbandonedOperations(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                var canDelete = false;
                using (var ownerLock = TryOpenExclusiveOwnerLock(directory))
                {
                    canDelete = ownerLock is not null;
                }
                if (!canDelete)
                    continue;
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static FileStream OpenOwnerLock(string operationDirectory) =>
        new(Path.Combine(operationDirectory, OwnerLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static FileStream? TryOpenExclusiveOwnerLock(string operationDirectory)
    {
        try
        {
            return OpenOwnerLock(operationDirectory);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDeleteOperationDirectory(string operationDirectory)
    {
        try
        {
            if (Directory.Exists(operationDirectory))
                Directory.Delete(operationDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void DeleteOperationDirectory(string operationDirectory) =>
        TryDeleteOperationDirectory(operationDirectory);
}

internal sealed class DownloadWorkspaceLease(string directory, FileStream ownerLock) : IDisposable
{
    private FileStream? ownerLock = ownerLock;

    public string Directory { get; } = directory;

    public void Dispose()
    {
        var lockHandle = Interlocked.Exchange(ref ownerLock, null);
        if (lockHandle is null)
            return;

        lockHandle.Dispose();
        DownloadWorkspace.DeleteOperationDirectory(Directory);
    }
}
