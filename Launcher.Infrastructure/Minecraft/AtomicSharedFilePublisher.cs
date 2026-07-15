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
        CancellationToken cancellationToken = default,
        string? managedRoot = null)
    {
        var result = await PublishCoreAsync(
            sourcePath,
            destinationPath,
            expectedSha1,
            replaceExisting: false,
            cancellationToken,
            managedRoot).ConfigureAwait(false);
        return result.Sha1;
    }

    public static Task<SharedFilePublishResult> PublishVerifiedReplacementAsync(
        string sourcePath,
        string destinationPath,
        string expectedSha1,
        CancellationToken cancellationToken = default,
        string? managedRoot = null)
    {
        if (!MinecraftFileIntegrity.IsSha1(expectedSha1))
            throw new ArgumentException("A valid trusted SHA-1 is required when replacing shared content.", nameof(expectedSha1));

        return PublishCoreAsync(
            sourcePath,
            destinationPath,
            expectedSha1,
            replaceExisting: true,
            cancellationToken,
            managedRoot);
    }

    private static async Task<SharedFilePublishResult> PublishCoreAsync(
        string sourcePath,
        string destinationPath,
        string? expectedSha1,
        bool replaceExisting,
        CancellationToken cancellationToken,
        string? managedRoot)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The shared runtime source file is missing.", sourcePath);
        if (!string.IsNullOrWhiteSpace(managedRoot))
            MinecraftPathGuard.EnsureSafeFileDestination(destinationPath, managedRoot, "Shared runtime destination");

        var destinationExisted = File.Exists(destinationPath);
        if (destinationExisted)
        {
            if (!string.IsNullOrWhiteSpace(managedRoot))
                MinecraftPathGuard.EnsureSafeFileDestination(destinationPath, managedRoot, "Shared runtime destination");
            if (!replaceExisting
                && await FilesMatchAsync(sourcePath, destinationPath, expectedSha1, cancellationToken).ConfigureAwait(false))
            {
                var existingSha1 = string.IsNullOrWhiteSpace(expectedSha1)
                    ? await Task.Run(() => ComputeSha1(sourcePath), cancellationToken).ConfigureAwait(false)
                    : expectedSha1;
                return new SharedFilePublishResult(existingSha1, SharedFilePublishDisposition.AlreadyMatched);
            }

            if (!replaceExisting)
                throw new IOException($"Shared runtime destination contains different content: {destinationPath}");
        }

        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new IOException($"Shared runtime destination has no parent directory: {destinationPath}");
        if (string.IsNullOrWhiteSpace(managedRoot))
        {
            Directory.CreateDirectory(parent);
        }
        else
        {
            MinecraftPathGuard.EnsureSafeFileDestination(destinationPath, managedRoot, "Shared runtime destination");
            MinecraftPathGuard.EnsureSafeDirectory(parent, managedRoot, "Shared runtime directory");
            MinecraftPathGuard.EnsureSafeFileDestination(destinationPath, managedRoot, "Shared runtime destination");
        }
        var temporaryPath = Path.Combine(parent, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        var published = false;

        void EnsureSafe()
        {
            if (string.IsNullOrWhiteSpace(managedRoot))
                return;
            MinecraftPathGuard.EnsureSafeFileDestination(destinationPath, managedRoot, "Shared runtime destination");
            MinecraftPathGuard.EnsureNoReparsePoints(managedRoot, temporaryPath, "Shared runtime temporary file");
        }

        try
        {
            EnsureSafe();
            var actualSha1 = await CopyAndFlushAsync(sourcePath, temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(expectedSha1)
                && !string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Shared runtime source SHA-1 did not match for {sourcePath}.");
            }

            if (replaceExisting)
            {
                EnsureSafe();
                if (File.Exists(destinationPath)
                    && await FilesMatchAsync(temporaryPath, destinationPath, actualSha1, cancellationToken).ConfigureAwait(false))
                {
                    return new SharedFilePublishResult(actualSha1, SharedFilePublishDisposition.AlreadyMatched);
                }

                var replacesExisting = File.Exists(destinationPath);
                if (replacesExisting
                    && (File.GetAttributes(destinationPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Shared runtime destination is a reparse point: {destinationPath}");
                }

                File.Move(temporaryPath, destinationPath, overwrite: true);
                published = true;
                return new SharedFilePublishResult(
                    actualSha1,
                    replacesExisting
                        ? SharedFilePublishDisposition.Replaced
                        : SharedFilePublishDisposition.Created);
            }
            else
            {
                EnsureSafe();
                try
                {
                    File.Move(temporaryPath, destinationPath, overwrite: false);
                    published = true;
                    return new SharedFilePublishResult(actualSha1, SharedFilePublishDisposition.Created);
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    if (!await FilesMatchAsync(temporaryPath, destinationPath, actualSha1, cancellationToken).ConfigureAwait(false))
                        throw new IOException($"Concurrent shared runtime publication produced different content: {destinationPath}");
                    return new SharedFilePublishResult(actualSha1, SharedFilePublishDisposition.AlreadyMatched);
                }
            }
        }

        finally
        {
            if (!published)
                TryDelete(temporaryPath, managedRoot);
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

    private static void TryDelete(string path, string? managedRoot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(managedRoot))
                MinecraftPathGuard.EnsureNoReparsePoints(managedRoot, path, "Shared runtime cleanup");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record SharedFilePublishResult(string Sha1, SharedFilePublishDisposition Disposition);

internal enum SharedFilePublishDisposition
{
    Created,
    AlreadyMatched,
    Replaced
}
