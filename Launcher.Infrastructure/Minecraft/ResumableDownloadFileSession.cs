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

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// A resumable session whose state only exists for one top-level download. No
/// part metadata or lock file survives the task that owns it.
/// </summary>
internal sealed class ResumableDownloadFileSession : IAsyncDisposable
{
    private const int BufferSize = 81920;
    private readonly string destinationPath;
    private readonly string temporaryPath;
    private readonly DownloadIntegrityExpectation integrity;
    private readonly DownloadPersistenceMode persistenceMode;
    private readonly MinecraftDownloadOperationContext? operationContext;
    private readonly PartFileCapacityManager.CapacityLease? capacityLease;
    private readonly IDisposable? assetLock;
    private string? sourceCandidateKey;
    private string? strongETag;
    private string? lastModified;
    private long? knownTotalLength;
    private bool disposed;

    private ResumableDownloadFileSession(
        string destinationPath,
        string temporaryPath,
        DownloadIntegrityExpectation integrity,
        DownloadPersistenceMode persistenceMode,
        MinecraftDownloadOperationContext? operationContext,
        PartFileCapacityManager.CapacityLease? capacityLease,
        IDisposable? assetLock)
    {
        this.destinationPath = destinationPath;
        this.temporaryPath = temporaryPath;
        this.integrity = integrity;
        this.persistenceMode = persistenceMode;
        this.operationContext = operationContext;
        this.capacityLease = capacityLease;
        this.assetLock = assetLock;
    }

    public bool IsComplete { get; private set; }
    public bool IsResumeRequested { get; private set; }

    public static Task<ResumableDownloadFileSession> AcquireAsync(
        string destinationPath,
        string? expectedSha1,
        long? expectedSize,
        string logicalResourceIdentity,
        CancellationToken cancellationToken,
        DownloadFileOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1) || !MinecraftFileIntegrity.IsSha1(expectedSha1))
            throw new InvalidDataException("A resumable game download requires a valid SHA-1 value.");
        return AcquireAsync(destinationPath, DownloadIntegrityExpectation.Sha1(expectedSha1, expectedSize),
            logicalResourceIdentity, cancellationToken, options);
    }

    public static async Task<ResumableDownloadFileSession> AcquireAsync(
        string destinationPath,
        DownloadIntegrityExpectation integrity,
        string logicalResourceIdentity,
        CancellationToken cancellationToken,
        DownloadFileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(integrity);
        _ = logicalResourceIdentity;
        options ??= new DownloadFileOptions();
        var normalizedDestination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(normalizedDestination)
            ?? throw new InvalidOperationException("Download destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        IDisposable? assetLock = null;
        PartFileCapacityManager.CapacityLease? capacityLease = null;
        try
        {
            if (options.PersistenceMode is DownloadPersistenceMode.LightweightAtomic)
            {
                assetLock = options.OperationContext is null
                    ? null
                    : await options.OperationContext.AcquireAssetLockAsync(normalizedDestination, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(normalizedDestination))
            {
                var trusted = options.PersistenceMode is DownloadPersistenceMode.LightweightAtomic
                    && options.OperationContext?.IsVerified(normalizedDestination, integrity) is true;
                if (trusted || await integrity.VerifyFileAsync(normalizedDestination, cancellationToken).ConfigureAwait(false))
                {
                    options.OperationContext?.MarkVerified(normalizedDestination, integrity);
                    return new ResumableDownloadFileSession(
                        normalizedDestination, string.Empty, integrity, options.PersistenceMode,
                        options.OperationContext, null, assetLock) { IsComplete = true };
                }
            }

            DeleteAbandonedTemporaryFiles(destinationDirectory, Path.GetFileName(normalizedDestination));
            var temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(normalizedDestination)}.bhl-pending-{Guid.NewGuid():N}.tmp");
            if (options.PersistenceMode is DownloadPersistenceMode.TaskScopedResumable)
                capacityLease = PartFileCapacityManager.Reserve(integrity.ExpectedSize);

            return new ResumableDownloadFileSession(
                normalizedDestination, temporaryPath, integrity, options.PersistenceMode,
                options.OperationContext, capacityLease, assetLock);
        }
        catch
        {
            capacityLease?.Dispose();
            assetLock?.Dispose();
            throw;
        }
    }

    public void ConfigureRequest(HttpRequestMessage request, ResolvedDownloadRequest resolution)
    {
        IsResumeRequested = false;
        if (persistenceMode is not DownloadPersistenceMode.TaskScopedResumable)
            return;

        var existingLength = GetTemporaryLength();
        var candidateKey = CreateSourceCandidateKey(resolution);
        if (existingLength <= 0 || knownTotalLength is null || existingLength >= knownTotalLength
            || sourceCandidateKey != candidateKey
            || string.IsNullOrWhiteSpace(strongETag) && string.IsNullOrWhiteSpace(lastModified))
            return;

        request.Headers.Range = new RangeHeaderValue(existingLength, null);
        request.Headers.TryAddWithoutValidation("If-Range", strongETag ?? lastModified);
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
        CancellationToken cancellationToken,
        Action<DownloadFileActivity>? reportActivity = null)
    {
        if (IsComplete)
            return;

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            reportActivity?.Invoke(DownloadFileActivity.Verifying);
            if (await VerifyTemporaryAsync(cancellationToken).ConfigureAwait(false))
            {
                Publish(cancellationToken);
                return;
            }
            DeleteTemporary();
            throw new DownloadContentValidationException("The server rejected the range request and the task-scoped file is not complete.");
        }

        var candidateKey = CreateSourceCandidateKey(resolution);
        var resumed = IsResumeRequested && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumed && !ValidatePartialResponse(response, candidateKey))
        {
            DeleteTemporary();
            throw new DownloadContentValidationException("The partial response did not match this task-scoped download session.");
        }
        if (!resumed)
            DeleteTemporary();

        var existingLength = resumed ? GetTemporaryLength() : 0;
        var totalLength = ResolveTotalLength(response, existingLength, resumed);
        knownTotalLength = totalLength;
        capacityLease?.SetExpectedSize(totalLength);
        sourceCandidateKey = candidateKey;
        strongETag = GetStrongETag(response);
        lastModified = GetLastModified(response);

        var hashers = integrity.CreateHashers();
        try
        {
            if (existingLength > 0)
                await AppendExistingTemporaryToHashAsync(hashers.Values, cancellationToken).ConfigureAwait(false);

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                temporaryPath,
                resumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            TryMarkTemporaryHidden(temporaryPath);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var persisted = existingLength;
            try
            {
                while (true)
                {
                    var read = await ReadNetworkAsync(source, buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    capacityLease?.BeforeWrite(read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    foreach (var hasher in hashers.Values)
                        hasher.AppendData(buffer, 0, read);
                    persisted += read;
                    reportAttemptProgress?.Invoke(attemptNumber, persisted, integrity.ExpectedSize ?? totalLength);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            reportActivity?.Invoke(DownloadFileActivity.Verifying);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
            await destination.DisposeAsync().ConfigureAwait(false);
            ValidateCompletedLength(persisted, totalLength);
            if (!integrity.Verify(hashers))
            {
                DeleteTemporary();
                throw new DownloadHashMismatchException("The downloaded file did not match its expected hash.");
            }
        }
        finally
        {
            foreach (var hasher in hashers.Values)
                hasher.Dispose();
        }

        reportActivity?.Invoke(DownloadFileActivity.Publishing);
        Publish(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;
        DeleteTemporary();
        capacityLease?.Dispose();
        assetLock?.Dispose();
        await Task.CompletedTask;
    }

    private bool ValidatePartialResponse(HttpResponseMessage response, string candidateKey)
    {
        var range = response.Content.Headers.ContentRange;
        var length = GetTemporaryLength();
        if (sourceCandidateKey != candidateKey || response.Content.Headers.ContentEncoding.Count != 0
            || range is null || range.Unit != "bytes" || range.From != length || range.To is null || range.Length is null
            || knownTotalLength != range.Length || response.Content.Headers.ContentLength != range.To - range.From + 1)
            return false;
        return strongETag is not null
            ? string.Equals(strongETag, GetStrongETag(response), StringComparison.Ordinal)
            : string.Equals(lastModified, GetLastModified(response), StringComparison.Ordinal);
    }

    private long? ResolveTotalLength(HttpResponseMessage response, long existingLength, bool resumed)
    {
        var total = resumed ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;
        if (total is null)
            return integrity.ExpectedSize;
        if (integrity.ExpectedSize.HasValue && integrity.ExpectedSize.Value != total.Value)
            throw new DownloadContentValidationException("The response total length did not match the expected file size.");
        return total;
    }

    private void ValidateCompletedLength(long persisted, long? totalLength)
    {
        var required = integrity.ExpectedSize ?? totalLength;
        if (required.HasValue && persisted != required.Value)
            throw new DownloadBodyInterruptedException("The response body length did not match the expected file size.");
    }

    private async Task<bool> VerifyTemporaryAsync(CancellationToken cancellationToken) =>
        File.Exists(temporaryPath) && await integrity.VerifyFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false);

    private async Task AppendExistingTemporaryToHashAsync(IEnumerable<IncrementalHash> hashers, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                foreach (var hasher in hashers)
                    hasher.AppendData(buffer, 0, read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private void Publish(CancellationToken cancellationToken)
    {
        try
        {
            using var publishLock = AcquireFinalPublishLock(destinationPath, cancellationToken);
            if (File.Exists(destinationPath)
                && integrity.VerifyFile(destinationPath, cancellationToken))
            {
                DeleteTemporary();
                operationContext?.MarkVerified(destinationPath, integrity);
                IsComplete = true;
                return;
            }
            ClearTemporaryHiddenAttribute();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            operationContext?.MarkVerified(destinationPath, integrity);
            IsComplete = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DownloadLocalFileException("Failed to atomically publish the completed download.", exception);
        }
    }

    private long GetTemporaryLength() => File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0;
    private void DeleteTemporary()
    {
        try
        {
            if (string.IsNullOrEmpty(temporaryPath) || !File.Exists(temporaryPath))
                return;
            File.Delete(temporaryPath);
            capacityLease?.DiscardRetainedBytes();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static void DeleteAbandonedTemporaryFiles(string directory, string destinationFileName)
    {
        var pattern = $".{destinationFileName}.bhl-pending-*.tmp";
        foreach (var candidate in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var lease = new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                lease.Close();
                File.Delete(candidate);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryMarkTemporaryHidden(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ClearTemporaryHiddenAttribute()
    {
        try
        {
            File.SetAttributes(temporaryPath, File.GetAttributes(temporaryPath) & ~FileAttributes.Hidden);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static string CreateSourceCandidateKey(ResolvedDownloadRequest resolution) { var uri = new Uri(resolution.ActualUrl, UriKind.Absolute); return $"{resolution.ResolvedSourceKind}:{uri.Host.ToLowerInvariant()}:{uri.AbsolutePath}"; }
    private static string? GetStrongETag(HttpResponseMessage response) { var tag = response.Headers.ETag?.Tag; return tag is not null && !response.Headers.ETag!.IsWeak ? tag : null; }
    private static string? GetLastModified(HttpResponseMessage response) => response.Content.Headers.LastModified?.ToString("R");

    private static async Task<int> ReadNetworkAsync(Stream source, byte[] buffer, CancellationToken cancellationToken)
    {
        try { return await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or HttpRequestException or OperationCanceledException)
        { throw new DownloadBodyInterruptedException("The response body was interrupted while downloading a file.", exception); }
    }

    private static IDisposable AcquireFinalPublishLock(string path, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path))));
        var mutex = new Mutex(false, $"Local\\BlockHelm.Download.Publish.{hash}");
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (mutex.WaitOne(TimeSpan.FromMilliseconds(50)))
                        return new PublishMutexLease(mutex);
                }
                catch (AbandonedMutexException)
                {
                    return new PublishMutexLease(mutex);
                }
            }
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class PublishMutexLease(Mutex mutex) : IDisposable
    {
        private Mutex? mutex = mutex;

        public void Dispose()
        {
            var ownedMutex = Interlocked.Exchange(ref mutex, null);
            if (ownedMutex is null)
                return;
            ownedMutex.ReleaseMutex();
            ownedMutex.Dispose();
        }
    }
}
