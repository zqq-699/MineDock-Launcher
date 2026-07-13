/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Buffers;
using System.IO;
using System.Security.Cryptography;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftFileIntegrity
{
    private const int BufferSize = 128 * 1024;

    public static bool IsSha1(string? value)
    {
        return value is { Length: 40 } && value.All(Uri.IsHexDigit);
    }

    public static MinecraftFileIntegrityStatus Evaluate(
        string path,
        string? expectedSha1,
        long? expectedSize,
        MinecraftFileVerification verification)
    {
        if (!File.Exists(path))
            return MinecraftFileIntegrityStatus.Missing;

        if (expectedSize is > 0 && new FileInfo(path).Length != expectedSize.Value)
            return MinecraftFileIntegrityStatus.SizeMismatch;

        if (verification == MinecraftFileVerification.Full
            && !string.IsNullOrWhiteSpace(expectedSha1)
            && !string.Equals(
                AtomicSharedFilePublisher.ComputeSha1(path),
                expectedSha1,
                StringComparison.OrdinalIgnoreCase))
        {
            return MinecraftFileIntegrityStatus.HashMismatch;
        }

        return MinecraftFileIntegrityStatus.Valid;
    }

    public static async Task<MinecraftFileIntegrityStatus> EvaluateAsync(
        string path,
        string? expectedSha1,
        long? expectedSize,
        MinecraftFileVerification verification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
            return MinecraftFileIntegrityStatus.Missing;

        if (expectedSize is > 0 && new FileInfo(path).Length != expectedSize.Value)
            return MinecraftFileIntegrityStatus.SizeMismatch;

        if (verification != MinecraftFileVerification.Full || string.IsNullOrWhiteSpace(expectedSha1))
            return MinecraftFileIntegrityStatus.Valid;

        var actualSha1 = await ComputeSha1Async(path, cancellationToken).ConfigureAwait(false);
        return string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase)
            ? MinecraftFileIntegrityStatus.Valid
            : MinecraftFileIntegrityStatus.HashMismatch;
    }

    public static bool IsValid(
        string path,
        string? expectedSha1,
        long? expectedSize,
        MinecraftFileVerification verification)
    {
        return Evaluate(path, expectedSha1, expectedSize, verification) == MinecraftFileIntegrityStatus.Valid;
    }

    public static async Task<bool> IsValidAsync(
        string path,
        string? expectedSha1,
        long? expectedSize,
        MinecraftFileVerification verification,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateAsync(path, expectedSha1, expectedSize, verification, cancellationToken)
            .ConfigureAwait(false) == MinecraftFileIntegrityStatus.Valid;
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                sha1.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(sha1.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal enum MinecraftFileVerification
{
    SizeOnly,
    Full
}

internal enum MinecraftFileIntegrityStatus
{
    Valid,
    Missing,
    SizeMismatch,
    HashMismatch
}
