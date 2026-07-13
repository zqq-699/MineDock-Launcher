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

internal static class AtomicSharedFilePublisher
{
    private const int BufferSize = 128 * 1024;

    public static async Task<string> PublishCopyAsync(
        string sourcePath,
        string destinationPath,
        string? expectedSha1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The shared runtime source file is missing.", sourcePath);

        if (File.Exists(destinationPath))
        {
            if (await FilesMatchAsync(sourcePath, destinationPath, expectedSha1, cancellationToken).ConfigureAwait(false))
            {
                return string.IsNullOrWhiteSpace(expectedSha1)
                    ? await Task.Run(() => ComputeSha1(sourcePath), cancellationToken).ConfigureAwait(false)
                    : expectedSha1;
            }

            throw new IOException($"Shared runtime destination contains different content: {destinationPath}");
        }

        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new IOException($"Shared runtime destination has no parent directory: {destinationPath}");
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.Combine(parent, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        var published = false;

        try
        {
            var actualSha1 = await CopyAndFlushAsync(sourcePath, temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(expectedSha1)
                && !string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Shared runtime source SHA-1 did not match for {sourcePath}.");
            }

            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: false);
                published = true;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                if (!await FilesMatchAsync(temporaryPath, destinationPath, actualSha1, cancellationToken).ConfigureAwait(false))
                    throw new IOException($"Concurrent shared runtime publication produced different content: {destinationPath}");
            }
            return actualSha1;
        }
        finally
        {
            if (!published)
                TryDelete(temporaryPath);
        }
    }

    public static string ComputeSha1(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA1.HashData(stream));
    }

    private static async Task<string> CopyAndFlushAsync(
        string sourcePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sha1.AppendData(buffer, 0, read);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
            return Convert.ToHexString(sha1.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<bool> FilesMatchAsync(
        string firstPath,
        string secondPath,
        string? knownFirstSha1,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = new FileInfo(firstPath);
        var second = new FileInfo(secondPath);
        if (first.Length != second.Length)
            return false;

        var firstSha1 = string.IsNullOrWhiteSpace(knownFirstSha1)
            ? await Task.Run(() => ComputeSha1(firstPath), cancellationToken).ConfigureAwait(false)
            : knownFirstSha1;
        var secondSha1 = await Task.Run(() => ComputeSha1(secondPath), cancellationToken).ConfigureAwait(false);
        return string.Equals(firstSha1, secondSha1, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
