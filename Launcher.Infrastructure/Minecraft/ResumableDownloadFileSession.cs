/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Owns one file target for the entire download budget.  The lock, part file and
/// metadata deliberately live beside the final file so another launcher process
/// cannot start a second writer for the same Windows-normalized target.
/// </summary>
internal sealed class ResumableDownloadFileSession : IAsyncDisposable
{
    private const int BufferSize = 81920;
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MetaWriteInterval = TimeSpan.FromSeconds(2);
    private const long MetaWriteBytesInterval = 1024 * 1024;

    private readonly string destinationPath;
    private readonly string partPath;
    private readonly string metaPath;
    private readonly DownloadIntegrityExpectation integrity;
    private readonly string logicalResourceIdentity;
    private readonly FileStream lockStream;
    private PartMetadata? metadata;
    private long lastMetaLength;
    private DateTimeOffset lastMetaWriteAt;

    private ResumableDownloadFileSession(
        string destinationPath,
        DownloadIntegrityExpectation integrity,
        string logicalResourceIdentity,
        FileStream lockStream)
    {
        this.destinationPath = destinationPath;
        partPath = destinationPath + ".part";
        metaPath = destinationPath + ".part.meta";
        this.integrity = integrity;
        this.logicalResourceIdentity = logicalResourceIdentity;
        this.lockStream = lockStream;
    }

    public bool IsComplete { get; private set; }

    public bool IsResumeRequested { get; private set; }

    public static async Task<ResumableDownloadFileSession> AcquireAsync(
        string destinationPath,
        string? expectedSha1,
        long? expectedSize,
        string logicalResourceIdentity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1) || expectedSha1.Length != 40 || expectedSha1.Any(value => !Uri.IsHexDigit(value)))
            throw new InvalidDataException("A resumable game download requires a valid SHA-1 value.");
        return await AcquireAsync(
            destinationPath,
            DownloadIntegrityExpectation.Sha1(expectedSha1, expectedSize),
            logicalResourceIdentity,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ResumableDownloadFileSession> AcquireAsync(
        string destinationPath,
        DownloadIntegrityExpectation integrity,
        string logicalResourceIdentity,
        CancellationToken cancellationToken)
    {

        var normalizedDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(normalizedDestination)
            ?? throw new InvalidOperationException("Download destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var lockStream = await AcquireLockAsync(normalizedDestination + ".part.lock", cancellationToken).ConfigureAwait(false);
        try
        {
            await PartFileCapacityManager.EnsureCapacityAsync(
                normalizedDestination,
                integrity.ExpectedSize,
                cancellationToken).ConfigureAwait(false);
            var session = new ResumableDownloadFileSession(
                normalizedDestination,
                integrity,
                logicalResourceIdentity,
                lockStream);
            await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            lockStream.Dispose();
            throw;
        }
    }

    public void ConfigureRequest(HttpRequestMessage request, ResolvedDownloadRequest resolution)
    {
        IsResumeRequested = false;
        var partLength = GetPartLength();
        var sourceKey = CreateSourceCandidateKey(resolution);
        if (partLength <= 0 || metadata is null || !CanResume(metadata, sourceKey, partLength))
            return;

        request.Headers.Range = new RangeHeaderValue(partLength, null);
        request.Headers.TryAddWithoutValidation("If-Range", metadata.StrongETag ?? metadata.LastModified);
        request.Headers.AcceptEncoding.Clear();
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        IsResumeRequested = true;
    }

    public async Task WriteAsync(
        HttpResponseMessage response,
        ResolvedDownloadRequest resolution,
        int attemptNumber,
        Action<long>? reportDownloadedBytes,
        Action<int, long, long?>? reportAttemptProgress,
        CancellationToken cancellationToken)
    {
        if (IsComplete)
            return;

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (await VerifyPartAsCompletedAsync(cancellationToken).ConfigureAwait(false))
                return;
            InvalidatePart();
            throw new DownloadContentValidationException("The server rejected the range request and the partial file is not complete.");
        }

        var sourceKey = CreateSourceCandidateKey(resolution);
        var resumed = IsResumeRequested && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumed && !ValidatePartialResponse(response, sourceKey))
        {
            InvalidatePart();
            throw new DownloadContentValidationException("The partial response did not match the persisted download identity.");
        }

        if (!resumed)
        {
            // A 200 after Range is explicitly a safe full restart, not a splice.
            InvalidatePart();
        }

        var existingLength = resumed ? GetPartLength() : 0;
        var totalLength = ResolveTotalLength(response, existingLength, resumed);
        if (totalLength is null)
        {
            // Persistent resume is intentionally disabled when the total cannot be proven.
            InvalidatePart();
            existingLength = 0;
        }

        metadata = new PartMetadata(
            logicalResourceIdentity,
            sourceKey,
            integrity.ExpectedSize ?? totalLength,
            integrity.Fingerprint,
            GetStrongETag(response),
            GetLastModified(response),
            existingLength,
            DateTimeOffset.UtcNow);
        await WriteMetadataAsync(force: true, cancellationToken).ConfigureAwait(false);

        var hashers = integrity.CreateHashers();
        if (existingLength > 0)
            await AppendExistingPartToHashAsync(hashers.Values, cancellationToken).ConfigureAwait(false);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            partPath,
            resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var persisted = existingLength;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                foreach (var hasher in hashers.Values)
                    hasher.AppendData(buffer, 0, read);
                persisted += read;
                reportDownloadedBytes?.Invoke(read);
                reportAttemptProgress?.Invoke(attemptNumber, persisted, integrity.ExpectedSize ?? totalLength);
                await WriteMetadataAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        await destination.DisposeAsync().ConfigureAwait(false);
        ValidateCompletedLength(response, persisted, totalLength);
        try
        {
            if (!integrity.Verify(hashers))
            {
                InvalidatePart();
                throw new DownloadHashMismatchException("The downloaded file did not match its expected hash.");
            }
        }
        finally
        {
            foreach (var hasher in hashers.Values)
                hasher.Dispose();
        }

        try
        {
            File.Move(partPath, destinationPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DownloadLocalFileException("Failed to atomically publish the completed download.", exception);
        }
        TryDelete(metaPath);
        IsComplete = true;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath) && await VerifyFileAsync(destinationPath, cancellationToken).ConfigureAwait(false))
        {
            IsComplete = true;
            return;
        }

        metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        var partLength = GetPartLength();
        if (partLength == 0 || metadata is null || metadata.LogicalResourceIdentity != logicalResourceIdentity
            || !string.Equals(metadata.ExpectedIntegrity, integrity.Fingerprint, StringComparison.Ordinal)
            || metadata.ExpectedSize != integrity.ExpectedSize)
        {
            InvalidatePart();
            metadata = null;
        }
    }

    private bool CanResume(PartMetadata value, string sourceKey, long partLength)
    {
        if (value.LogicalResourceIdentity != logicalResourceIdentity
            || !string.Equals(value.SourceCandidateKey, sourceKey, StringComparison.Ordinal)
            || !string.Equals(value.ExpectedIntegrity, integrity.Fingerprint, StringComparison.Ordinal)
            || value.ExpectedSize != integrity.ExpectedSize
            || partLength <= 0
            || value.ExpectedSize is null
            || partLength >= value.ExpectedSize)
            return false;
        return !string.IsNullOrWhiteSpace(value.StrongETag)
            || !string.IsNullOrWhiteSpace(value.LastModified);
    }

    private bool ValidatePartialResponse(HttpResponseMessage response, string sourceKey)
    {
        if (metadata is null || metadata.SourceCandidateKey != sourceKey || response.Content.Headers.ContentEncoding.Count != 0)
            return false;
        var range = response.Content.Headers.ContentRange;
        var length = GetPartLength();
        if (range is null || range.Unit != "bytes" || range.From != length || range.To is null || range.Length is null)
            return false;
        if (metadata.ExpectedSize != range.Length || response.Content.Headers.ContentLength != range.To - range.From + 1)
            return false;
        var etag = GetStrongETag(response);
        return metadata.StrongETag is not null
            ? string.Equals(metadata.StrongETag, etag, StringComparison.Ordinal)
            : string.Equals(metadata.LastModified, GetLastModified(response), StringComparison.Ordinal);
    }

    private long? ResolveTotalLength(HttpResponseMessage response, long existingLength, bool resumed)
    {
        var responseLength = resumed ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;
        if (responseLength is null)
            return null;
        if (integrity.ExpectedSize.HasValue && integrity.ExpectedSize.Value != responseLength.Value)
            throw new DownloadContentValidationException("The response total length did not match the expected file size.");
        return responseLength;
    }

    private void ValidateCompletedLength(HttpResponseMessage response, long persisted, long? totalLength)
    {
        var required = integrity.ExpectedSize ?? totalLength;
        if (required.HasValue && persisted != required.Value)
            throw new DownloadBodyInterruptedException("The response body length did not match the expected file size.");
    }

    private async Task<bool> VerifyPartAsCompletedAsync(CancellationToken cancellationToken)
    {
        if (integrity.ExpectedSize is null || GetPartLength() != integrity.ExpectedSize.Value)
            return false;
        if (!await VerifyFileAsync(partPath, cancellationToken).ConfigureAwait(false))
            return false;
        File.Move(partPath, destinationPath, overwrite: true);
        TryDelete(metaPath);
        IsComplete = true;
        return true;
    }

    private async Task<bool> VerifyFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return false;
        return await integrity.VerifyFileAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendExistingPartToHashAsync(IEnumerable<IncrementalHash> hashers, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                foreach (var hasher in hashers)
                    hasher.AppendData(buffer, 0, read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private async Task<PartMetadata?> ReadMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(metaPath);
            return await JsonSerializer.DeserializeAsync<PartMetadata>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteMetadataAsync(bool force, CancellationToken cancellationToken)
    {
        if (metadata is null) return;
        var actualLength = GetPartLength();
        var now = DateTimeOffset.UtcNow;
        if (!force && actualLength - lastMetaLength < MetaWriteBytesInterval && now - lastMetaWriteAt < MetaWriteInterval)
            return;
        metadata = metadata with { DiagnosticLength = actualLength, UpdatedAt = now };
        var temporary = metaPath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, metaPath, overwrite: true);
        lastMetaLength = actualLength;
        lastMetaWriteAt = now;
    }

    private void InvalidatePart()
    {
        TryDelete(partPath);
        TryDelete(metaPath);
    }

    private long GetPartLength() => File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
    private static string CreateSourceCandidateKey(ResolvedDownloadRequest resolution)
    {
        var uri = new Uri(resolution.ActualUrl, UriKind.Absolute);
        return $"{resolution.ResolvedSourceKind}:{uri.Host.ToLowerInvariant()}:{uri.AbsolutePath}";
    }
    private static string? GetStrongETag(HttpResponseMessage response)
    {
        var tag = response.Headers.ETag?.Tag;
        return tag is not null && !response.Headers.ETag!.IsWeak ? tag : null;
    }
    private static string? GetLastModified(HttpResponseMessage response) => response.Content.Headers.LastModified?.ToString("R");
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static async Task<FileStream> AcquireLockAsync(string lockPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous); }
            catch (IOException) { await Task.Delay(LockPollInterval, cancellationToken).ConfigureAwait(false); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lockStream.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record PartMetadata(
        string LogicalResourceIdentity,
        string SourceCandidateKey,
        long? ExpectedSize,
        string ExpectedIntegrity,
        string? StrongETag,
        string? LastModified,
        long DiagnosticLength,
        DateTimeOffset UpdatedAt);
}
