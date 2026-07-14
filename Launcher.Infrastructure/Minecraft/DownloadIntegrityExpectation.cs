/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Security.Cryptography;
using System.IO;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadIntegrityExpectation
{
    private readonly IReadOnlyDictionary<HashAlgorithmName, byte[]> requiredHashes;

    public DownloadIntegrityExpectation(long? expectedSize, IEnumerable<(HashAlgorithmName Algorithm, string Value)> hashes)
    {
        ExpectedSize = expectedSize;
        requiredHashes = hashes
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .ToDictionary(value => value.Algorithm, value => Convert.FromHexString(value.Value.Trim()));
        if (requiredHashes.Count == 0)
            throw new InvalidDataException("A resumable download requires at least one expected hash.");
    }

    public long? ExpectedSize { get; }
    public bool HasStrongHash => requiredHashes.Keys.Any(IsStrong);
    public string Fingerprint => string.Join(
        ";",
        requiredHashes.OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Name}:{Convert.ToHexString(pair.Value)}"));

    public static DownloadIntegrityExpectation Sha1(string value, long? expectedSize) =>
        new(expectedSize, [(HashAlgorithmName.SHA1, value)]);

    public IReadOnlyDictionary<HashAlgorithmName, IncrementalHash> CreateHashers() =>
        requiredHashes.Keys.ToDictionary(algorithm => algorithm, IncrementalHash.CreateHash);

    public bool Verify(IReadOnlyDictionary<HashAlgorithmName, IncrementalHash> hashers)
    {
        return requiredHashes.All(pair => hashers.TryGetValue(pair.Key, out var hasher)
            && CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), pair.Value));
    }

    public async Task<bool> VerifyFileAsync(string path, CancellationToken cancellationToken)
    {
        if (ExpectedSize.HasValue && new FileInfo(path).Length != ExpectedSize.Value)
            return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hashers = CreateHashers();
        var buffer = new byte[81920];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                foreach (var hasher in hashers.Values)
                    hasher.AppendData(buffer, 0, read);
            }
            return Verify(hashers);
        }
        finally
        {
            foreach (var hasher in hashers.Values)
                hasher.Dispose();
        }
    }

    private static bool IsStrong(HashAlgorithmName algorithm) => algorithm.Name is "SHA1" or "SHA256" or "SHA512";
}
