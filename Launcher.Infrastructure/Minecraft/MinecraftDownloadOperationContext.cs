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
    public MinecraftDownloadOperationContext(string managedRoot)
    {
        ManagedRoot = Path.GetFullPath(managedRoot);
    }

    public string ManagedRoot { get; }

    public void Dispose()
    {
    }

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
