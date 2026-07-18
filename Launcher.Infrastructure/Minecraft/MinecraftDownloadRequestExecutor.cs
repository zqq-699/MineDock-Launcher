/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Xml;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 按下载源优先级执行请求，并统一处理并发租约、重试、源切换和“确实不存在”的查询结果。
/// </summary>
internal sealed class MinecraftDownloadRequestExecutor
{
    internal const long MinimumSegmentedDownloadSize = 8L * 1024 * 1024;
    internal const int SegmentedDownloadPartCount = 4;

    private readonly MinecraftDownloadTransport transport;
    private readonly ILogger logger;
    private readonly DownloadBandwidthLimiter? bandwidthLimiter;
    private readonly IImportConcurrencyLimiter limiter;
    private readonly DownloadConcurrencyCategory category;
    private readonly DownloadRetryOptions retryOptions;
    private readonly DownloadHostHealthTracker hostHealthTracker;
    private readonly DownloadHostConcurrencyController hostConcurrencyController;
    private readonly BmclApiRequestRateLimiter bmclApiRequestRateLimiter;
    private readonly Func<double> nextRetryJitter;
    private readonly TimeProvider timeProvider;

    public MinecraftDownloadRequestExecutor(
        HttpClient httpClient,
        ILogger? logger = null,
        DownloadBandwidthLimiter? bandwidthLimiter = null,
        IImportConcurrencyLimiter? limiter = null,
        DownloadConcurrencyCategory category = DownloadConcurrencyCategory.Metadata,
        DownloadRetryOptions? retryOptions = null,
        DownloadHostHealthTracker? hostHealthTracker = null,
        DownloadHostConcurrencyController? hostConcurrencyController = null,
        Func<double>? nextRetryJitter = null,
        BmclApiRequestRateLimiter? bmclApiRequestRateLimiter = null,
        TimeProvider? timeProvider = null)
    {
        this.logger = logger ?? NullLogger.Instance;
        this.bandwidthLimiter = bandwidthLimiter;
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
        this.category = category;
        this.retryOptions = retryOptions ?? DownloadRetryOptions.Default;
        // A batch may provide a shared tracker; the default remains executor
        // scoped so unrelated operations never inherit a stale cooldown.
        this.hostHealthTracker = hostHealthTracker ?? new DownloadHostHealthTracker();
        this.hostConcurrencyController = hostConcurrencyController ?? DownloadHostConcurrencyController.Shared;
        this.bmclApiRequestRateLimiter = bmclApiRequestRateLimiter ?? BmclApiRequestRateLimiter.Shared;
        this.nextRetryJitter = nextRetryJitter ?? Random.Shared.NextDouble;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        transport = new MinecraftDownloadTransport(
            httpClient,
            this.retryOptions,
            AcquireAdmissionAsync);
    }

    public async Task<T> ExecuteAsync<T>(
        string originalUrl,
        DownloadSourcePreference preference,
        string? categoryHint,
        Func<DownloadAttemptContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteCoreAsync(
            originalUrl,
            preference,
            categoryHint,
            operation,
            noResultStatus: null,
            lookupMode: false,
            configureRequest: null,
            allowResponseStatus: null,
            sensitiveHeaders: null,
            speedMeter: null,
            executionMode: DownloadExecutionMode.PerSourceRetries,
            fileCandidates: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value!;
    }

    public Task<DownloadLookupResult<T>> ExecuteLookupAsync<T>(
        string originalUrl,
        DownloadSourcePreference preference,
        string? categoryHint,
        Func<DownloadAttemptContext, CancellationToken, Task<T>> operation,
        Func<HttpStatusCode, bool> noResultStatus,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(
            originalUrl,
            preference,
            categoryHint,
            operation,
            noResultStatus,
            lookupMode: true,
            configureRequest: null,
            allowResponseStatus: null,
            sensitiveHeaders: null,
            speedMeter: null,
            executionMode: DownloadExecutionMode.PerSourceRetries,
            fileCandidates: null,
            cancellationToken: cancellationToken);
    }

    public Task<ResolvedDownloadRequest> DownloadFileAsync(
        string originalUrl,
        DownloadSourcePreference preference,
        string? categoryHint,
        string destinationPath,
        string? expectedSha1,
        long? expectedSize,
        CancellationToken cancellationToken,
        Action<int, long, long?>? reportAttemptProgress = null,
        DownloadRequestHeaders? sensitiveHeaders = null,
        DownloadFileOptions? options = null,
        SpeedMeter? speedMeter = null) =>
        DownloadFileAsync(
            [originalUrl],
            preference,
            categoryHint,
            destinationPath,
            expectedSha1,
            expectedSize,
            cancellationToken,
            reportAttemptProgress,
            sensitiveHeaders,
            options,
            speedMeter);

    public Task<ResolvedDownloadRequest> DownloadFileAsync(
        IReadOnlyList<string> originalUrls,
        DownloadSourcePreference preference,
        string? categoryHint,
        string destinationPath,
        string? expectedSha1,
        long? expectedSize,
        CancellationToken cancellationToken,
        Action<int, long, long?>? reportAttemptProgress = null,
        DownloadRequestHeaders? sensitiveHeaders = null,
        DownloadFileOptions? options = null,
        SpeedMeter? speedMeter = null)
    {
        var fileCandidates = ResolveFileCandidates(originalUrls, preference, categoryHint);
        var originalUrl = fileCandidates[0].OriginalUrl;

        // Some legacy installer metadata has no trusted hash. Preserve its existing
        // atomic one-shot behavior, but never persist it as a resumable part.
        if (string.IsNullOrWhiteSpace(expectedSha1))
        {
            var managedRoot = options?.ResolveManagedRoot(destinationPath);
            MinecraftDownloadFileWriter.PrepareDestination(destinationPath, expectedSha1, managedRoot);
            return DownloadUnverifiedAsync();

            async Task<ResolvedDownloadRequest> DownloadUnverifiedAsync()
            {
                var result = await ExecuteCoreAsync(
                    originalUrl,
                    preference,
                    categoryHint,
                    async (context, token) =>
                    {
                        await MinecraftDownloadFileWriter.WriteAsync(
                            context.Response,
                            destinationPath,
                            expectedSha1,
                            expectedSize,
                            context.AttemptNumber,
                            reportAttemptProgress,
                            token,
                            managedRoot).ConfigureAwait(false);
                        return context.Resolution;
                    },
                    noResultStatus: null,
                    lookupMode: false,
                    configureRequest: null,
                    allowResponseStatus: null,
                    sensitiveHeaders,
                    speedMeter: speedMeter,
                    executionMode: DownloadExecutionMode.FileSourceRounds,
                    fileCandidates: fileCandidates,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return result.Value!;
            }
        }

        return DownloadFileCoreAsync();

        async Task<ResolvedDownloadRequest> DownloadFileCoreAsync()
        {
            var integrity = DownloadIntegrityExpectation.Sha1(expectedSha1, expectedSize);
            await using var session = await ResumableDownloadFileSession.AcquireAsync(
                destinationPath,
                integrity,
                cancellationToken,
                options).ConfigureAwait(false);
            if (session.IsComplete)
                return fileCandidates[0];

            var segmented = await TryDownloadSegmentedAsync(
                fileCandidates,
                integrity,
                options,
                session,
                reportAttemptProgress,
                sensitiveHeaders,
                speedMeter,
                cancellationToken).ConfigureAwait(false);
            if (segmented.Resolution is not null)
                return segmented.Resolution;

            var sequentialProgress = OffsetAttemptProgress(reportAttemptProgress, segmented.Attempted ? 1 : 0);

            var result = await ExecuteCoreAsync(
                originalUrl,
                preference,
                categoryHint,
                async (context, token) =>
                {
                    await session.WriteAsync(
                        context.Response,
                        context.Resolution,
                        context.AttemptNumber,
                        sequentialProgress,
                        token).ConfigureAwait(false);
                    return context.Resolution;
                },
                noResultStatus: null,
                lookupMode: false,
                configureRequest: (request, resolution) => session.ConfigureRequest(request, resolution),
                allowResponseStatus: status => status == HttpStatusCode.RequestedRangeNotSatisfiable,
                sensitiveHeaders: sensitiveHeaders,
                speedMeter: speedMeter,
                executionMode: DownloadExecutionMode.FileSourceRounds,
                fileCandidates: fileCandidates,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Value!;
        }
    }

    public Task<ResolvedDownloadRequest> DownloadFileAsync(
        string originalUrl,
        DownloadSourcePreference preference,
        string? categoryHint,
        string destinationPath,
        DownloadIntegrityExpectation integrity,
        CancellationToken cancellationToken,
        DownloadRequestHeaders? sensitiveHeaders = null,
        Action<int, long, long?>? reportAttemptProgress = null,
        DownloadFileOptions? options = null,
        SpeedMeter? speedMeter = null) =>
        DownloadFileAsync(
            [originalUrl],
            preference,
            categoryHint,
            destinationPath,
            integrity,
            cancellationToken,
            sensitiveHeaders,
            reportAttemptProgress,
            options,
            speedMeter);

    public Task<ResolvedDownloadRequest> DownloadFileAsync(
        IReadOnlyList<string> originalUrls,
        DownloadSourcePreference preference,
        string? categoryHint,
        string destinationPath,
        DownloadIntegrityExpectation integrity,
        CancellationToken cancellationToken,
        DownloadRequestHeaders? sensitiveHeaders = null,
        Action<int, long, long?>? reportAttemptProgress = null,
        DownloadFileOptions? options = null,
        SpeedMeter? speedMeter = null)
    {
        var fileCandidates = ResolveFileCandidates(originalUrls, preference, categoryHint);
        var originalUrl = fileCandidates[0].OriginalUrl;
        return DownloadCoreAsync();

        async Task<ResolvedDownloadRequest> DownloadCoreAsync()
        {
            await using var session = await ResumableDownloadFileSession.AcquireAsync(
                destinationPath,
                integrity,
                cancellationToken,
                options).ConfigureAwait(false);
            if (session.IsComplete)
                return fileCandidates[0];

            var segmented = await TryDownloadSegmentedAsync(
                fileCandidates,
                integrity,
                options,
                session,
                reportAttemptProgress,
                sensitiveHeaders,
                speedMeter,
                cancellationToken).ConfigureAwait(false);
            if (segmented.Resolution is not null)
                return segmented.Resolution;

            var sequentialProgress = OffsetAttemptProgress(reportAttemptProgress, segmented.Attempted ? 1 : 0);

            var result = await ExecuteCoreAsync(
                originalUrl,
                preference,
                categoryHint,
                async (context, token) =>
                {
                    await session.WriteAsync(context.Response, context.Resolution, context.AttemptNumber, sequentialProgress, token).ConfigureAwait(false);
                    return context.Resolution;
                },
                noResultStatus: null,
                lookupMode: false,
                configureRequest: (request, resolution) => session.ConfigureRequest(request, resolution),
                allowResponseStatus: status => status == HttpStatusCode.RequestedRangeNotSatisfiable,
                sensitiveHeaders,
                speedMeter: speedMeter,
                executionMode: DownloadExecutionMode.FileSourceRounds,
                fileCandidates: fileCandidates,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Value!;
        }
    }

    private async Task<SegmentedDownloadAttemptResult> TryDownloadSegmentedAsync(
        IReadOnlyList<ResolvedDownloadRequest> candidates,
        DownloadIntegrityExpectation integrity,
        DownloadFileOptions? options,
        ResumableDownloadFileSession session,
        Action<int, long, long?>? reportAttemptProgress,
        DownloadRequestHeaders? sensitiveHeaders,
        SpeedMeter? speedMeter,
        CancellationToken cancellationToken)
    {
        if (!ShouldUseSegmentedDownload(candidates, integrity, options))
            return default;

        var resolution = candidates[0];
        var totalLength = integrity.ExpectedSize!.Value;
        var ranges = CreateSegmentRanges(totalLength);
        var tasks = new List<Task>(SegmentedDownloadPartCount);
        using var segmentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            session.PrepareSegmentedDownload(totalLength);
            logger.LogInformation(
                "Segmented download started. ResolvedSourceKind={ResolvedSourceKind} FileSize={FileSize} SegmentCount={SegmentCount}",
                resolution.ResolvedSourceKind,
                totalLength,
                ranges.Count);

            var firstAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstTask = DownloadSegmentAsync(
                resolution,
                ranges[0],
                session,
                reportAttemptProgress,
                sensitiveHeaders,
                speedMeter,
                () => firstAccepted.TrySetResult(true),
                segmentCancellation.Token);
            tasks.Add(firstTask);

            await Task.WhenAny(firstAccepted.Task, firstTask).ConfigureAwait(false);
            if (!firstAccepted.Task.IsCompletedSuccessfully)
                await firstTask.ConfigureAwait(false);

            for (var index = 1; index < ranges.Count; index++)
            {
                tasks.Add(DownloadSegmentAsync(
                    resolution,
                    ranges[index],
                    session,
                    reportAttemptProgress,
                    sensitiveHeaders,
                    speedMeter,
                    segmentAccepted: null,
                    segmentCancellation.Token));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            await session.CompleteSegmentedDownloadAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Segmented download completed. ResolvedSourceKind={ResolvedSourceKind} FileSize={FileSize} SegmentCount={SegmentCount}",
                resolution.ResolvedSourceKind,
                totalLength,
                ranges.Count);
            return new SegmentedDownloadAttemptResult(true, resolution);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            segmentCancellation.Cancel();
            await ObserveSegmentTasksAsync(tasks).ConfigureAwait(false);
            session.ResetSegmentedDownload();
            throw;
        }
        catch (Exception exception)
        {
            segmentCancellation.Cancel();
            await ObserveSegmentTasksAsync(tasks).ConfigureAwait(false);
            session.ResetSegmentedDownload();
            logger.LogInformation(
                "Segmented download fell back to the existing single-stream path. ResolvedSourceKind={ResolvedSourceKind} FileSize={FileSize} SegmentCount={SegmentCount} Reason={Reason}",
                resolution.ResolvedSourceKind,
                totalLength,
                ranges.Count,
                exception.GetType().Name);
            return new SegmentedDownloadAttemptResult(true, null);
        }
    }

    private Task DownloadSegmentAsync(
        ResolvedDownloadRequest resolution,
        DownloadSegmentRange range,
        ResumableDownloadFileSession session,
        Action<int, long, long?>? reportAttemptProgress,
        DownloadRequestHeaders? sensitiveHeaders,
        SpeedMeter? speedMeter,
        Action? segmentAccepted,
        CancellationToken cancellationToken)
    {
        return ExecuteAttemptAsync(
            resolution,
            attempt: 1,
            async (context, token) =>
            {
                ValidateSegmentResponse(context.Response, range);
                segmentAccepted?.Invoke();
                await session.WriteSegmentAsync(
                    context.Response.Content,
                    range.Start,
                    range.End,
                    attemptNumber: 1,
                    reportAttemptProgress,
                    token).ConfigureAwait(false);
                return true;
            },
            noResultStatus: null,
            cancellationToken,
            configureRequest: (request, _) => ConfigureSegmentRequest(request, range),
            allowResponseStatus: null,
            sensitiveHeaders,
            speedMeter,
            enableSlowBodyWatchdog: true);
    }

    private bool ShouldUseSegmentedDownload(
        IReadOnlyList<ResolvedDownloadRequest> candidates,
        DownloadIntegrityExpectation integrity,
        DownloadFileOptions? options)
    {
        if (integrity.ExpectedSize is not >= MinimumSegmentedDownloadSize || !integrity.HasStrongHash)
            return false;
        if ((options?.PersistenceMode ?? DownloadPersistenceMode.TaskScopedResumable)
            is not DownloadPersistenceMode.TaskScopedResumable)
            return false;
        if (GetGlobalConcurrencySnapshot().CurrentTarget < SegmentedDownloadPartCount)
            return false;

        var uri = new Uri(candidates[0].ActualUrl, UriKind.Absolute);
        return !uri.Host.Equals("bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DownloadSegmentRange> CreateSegmentRanges(long totalLength)
    {
        var ranges = new List<DownloadSegmentRange>(SegmentedDownloadPartCount);
        var baseLength = totalLength / SegmentedDownloadPartCount;
        var remainder = totalLength % SegmentedDownloadPartCount;
        long start = 0;
        for (var index = 0; index < SegmentedDownloadPartCount; index++)
        {
            var length = baseLength + (index < remainder ? 1 : 0);
            var end = start + length - 1;
            ranges.Add(new DownloadSegmentRange(start, end, totalLength));
            start = end + 1;
        }
        return ranges;
    }

    private static void ConfigureSegmentRequest(HttpRequestMessage request, DownloadSegmentRange range)
    {
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(range.Start, range.End);
        request.Headers.AcceptEncoding.Clear();
        request.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("identity"));
    }

    private static void ValidateSegmentResponse(HttpResponseMessage response, DownloadSegmentRange expected)
    {
        var contentRange = response.Content.Headers.ContentRange;
        var expectedLength = expected.End - expected.Start + 1;
        if (response.StatusCode != HttpStatusCode.PartialContent
            || response.Content.Headers.ContentEncoding.Count != 0
            || contentRange is null
            || !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
            || contentRange.From != expected.Start
            || contentRange.To != expected.End
            || contentRange.Length != expected.TotalLength
            || response.Content.Headers.ContentLength != expectedLength)
        {
            throw new SegmentedDownloadNotSupportedException();
        }
    }

    private static async Task ObserveSegmentTasksAsync(IEnumerable<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static Action<int, long, long?>? OffsetAttemptProgress(
        Action<int, long, long?>? progress,
        int offset)
    {
        if (progress is null || offset == 0)
            return progress;
        return (attempt, progressedBytes, totalBytes) =>
            progress(attempt + offset, progressedBytes, totalBytes);
    }

    private readonly record struct DownloadSegmentRange(long Start, long End, long TotalLength);
    private readonly record struct SegmentedDownloadAttemptResult(bool Attempted, ResolvedDownloadRequest? Resolution);
    private sealed class SegmentedDownloadNotSupportedException : Exception;

    /// <summary>
    /// 按解析后的候选源和每源重试次数执行操作，并汇总所有可恢复失败用于最终诊断。
    /// </summary>
    private async Task<DownloadLookupResult<T>> ExecuteCoreAsync<T>(
        string originalUrl,
        DownloadSourcePreference preference,
        string? categoryHint,
        Func<DownloadAttemptContext, CancellationToken, Task<T>> operation,
        Func<HttpStatusCode, bool>? noResultStatus,
        bool lookupMode,
        Action<HttpRequestMessage, ResolvedDownloadRequest>? configureRequest,
        Func<HttpStatusCode, bool>? allowResponseStatus,
        DownloadRequestHeaders? sensitiveHeaders,
        SpeedMeter? speedMeter,
        DownloadExecutionMode executionMode,
        IReadOnlyList<ResolvedDownloadRequest>? fileCandidates,
        CancellationToken cancellationToken)
    {
        if (executionMode is DownloadExecutionMode.FileSourceRounds)
        {
            if (lookupMode || noResultStatus is not null)
                throw new InvalidOperationException("File source rounds do not support lookup requests.");

            return await ExecuteFileSourceRoundsAsync(
                originalUrl,
                fileCandidates ?? throw new InvalidOperationException("File source rounds require resolved candidates."),
                operation,
                configureRequest,
                allowResponseStatus,
                sensitiveHeaders,
                speedMeter,
                cancellationToken).ConfigureAwait(false);
        }

        // 候选顺序已经包含用户偏好和镜像回退策略；每个源内部重试耗尽后才切换下一源。
        var candidates = MinecraftDownloadSourceResolver
            .EnumerateRequests(originalUrl, preference, categoryHint)
            .ToList();
        var failures = new List<Exception>();
        var noResultSourceCount = 0;
        ResolvedDownloadRequest? lastResolution = null;

        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var resolution = candidates[candidateIndex];
            lastResolution = resolution;
            var sourceReportedNoResult = false;
            var slowDisconnectCount = 0;
            int? slowWatchdogDisabledAttempt = null;

            for (var attempt = 1; attempt <= retryOptions.MaxAttemptsPerSource; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt == 1
                    && !resolution.ResolvedSourceKind.StartsWith("BmclApi", StringComparison.Ordinal)
                    && hostHealthTracker.ShouldAvoid(resolution.ResolvedSourceKind, GetCandidateHost(resolution)))
                {
                    var avoided = new DownloadAttemptException(
                        DownloadFailureDisposition.SwitchSource,
                        DownloadFailureReason.Network,
                        "The download host is temporarily avoided after repeated transient failures.");
                    failures.Add(avoided);
                    LogFailure(resolution, attempt, avoided, retryCurrentSource: false);
                    break;
                }

                try
                {
                    var value = await ExecuteAttemptAsync(
                        resolution,
                        attempt,
                        operation,
                        noResultStatus,
                        cancellationToken,
                        configureRequest,
                        allowResponseStatus,
                        sensitiveHeaders,
                        speedMeter,
                        enableSlowBodyWatchdog: slowWatchdogDisabledAttempt != attempt).ConfigureAwait(false);
                    hostHealthTracker.RecordSuccess(resolution.ResolvedSourceKind, GetCandidateHost(resolution));
                    LogResolvedRequest(resolution, attempt);
                    return DownloadLookupResult<T>.Success(value);
                }
                catch (DownloadNoResultException exception)
                {
                    sourceReportedNoResult = true;
                    logger.LogInformation(
                        exception,
                        "Download source explicitly reported no result. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} Attempt={Attempt}",
                        preference,
                        resolution.ResourceCategory,
                        DownloadUriLogSanitizer.Sanitize(resolution.OriginalUrl),
                        DownloadUriLogSanitizer.Sanitize(resolution.ActualUrl),
                        resolution.ResolvedSourceKind,
                        attempt);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    var failure = ClassifyException(exception);
                    if (failure.Reason is DownloadFailureReason.BodyTooSlow)
                    {
                        slowDisconnectCount++;
                        if (slowDisconnectCount >= 2)
                        {
                            if (candidateIndex + 1 < candidates.Count)
                            {
                                failure.WithDisposition(DownloadFailureDisposition.SwitchSource);
                            }
                            else if (attempt < retryOptions.MaxAttemptsPerSource)
                            {
                                slowWatchdogDisabledAttempt = attempt + 1;
                                failure.WithDisposition(DownloadFailureDisposition.RetryCurrentSource);
                                logger.LogInformation(
                                    "Download slow-body watchdog disabled for the final fallback attempt. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} Attempt={Attempt}",
                                    preference,
                                    resolution.ResourceCategory,
                                    DownloadUriLogSanitizer.Sanitize(resolution.ActualUrl),
                                    resolution.ResolvedSourceKind,
                                    attempt + 1);
                            }
                        }
                    }
                    RecordHostResult(resolution, failure);
                    hostHealthTracker.RecordFailure(
                        resolution.ResolvedSourceKind,
                        failure.FinalHost ?? GetCandidateHost(resolution),
                        failure.Reason,
                        failure.StatusCode);
                    // Abort 表示请求本身或本地写入不可通过换源修复；其余分类决定重试当前源还是直接换源。
                    if (failure.Disposition is DownloadFailureDisposition.Abort)
                        throw failure;

                    failures.Add(failure);
                    var retryCurrentSource = failure.Disposition is DownloadFailureDisposition.RetryCurrentSource
                        && attempt < retryOptions.MaxAttemptsPerSource;
                    LogFailure(resolution, attempt, failure, retryCurrentSource);

                    if (!retryCurrentSource)
                        break;

                    await Task.Delay(GetRetryDelay(failure, attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            if (sourceReportedNoResult)
                noResultSourceCount++;
        }

        if (lookupMode && noResultSourceCount == candidates.Count)
            return DownloadLookupResult<T>.NotFound();

        var finalResolution = lastResolution
            ?? throw new InvalidOperationException($"No download candidates were available for {originalUrl}.");
        var finalException = failures.LastOrDefault()
            ?? new InvalidOperationException($"No download source returned a usable result for {originalUrl}.");
        throw new DownloadSourceRequestException(finalResolution, finalException, failures);
    }

    private async Task<DownloadLookupResult<T>> ExecuteFileSourceRoundsAsync<T>(
        string originalUrl,
        IReadOnlyList<ResolvedDownloadRequest> candidates,
        Func<DownloadAttemptContext, CancellationToken, Task<T>> operation,
        Action<HttpRequestMessage, ResolvedDownloadRequest>? configureRequest,
        Func<HttpStatusCode, bool>? allowResponseStatus,
        DownloadRequestHeaders? sensitiveHeaders,
        SpeedMeter? speedMeter,
        CancellationToken cancellationToken)
    {
        var activeCandidateIndexes = Enumerable.Range(0, candidates.Count).ToList();
        var failures = new List<Exception>();
        var maxRounds = Math.Max(1, retryOptions.MaxFileSourceRounds);
        ResolvedDownloadRequest? lastResolution = null;

        for (var round = 1; round <= maxRounds && activeCandidateIndexes.Count > 0; round++)
        {
            var nextRoundCandidateIndexes = new List<int>();
            var nextRoundFailures = new List<DownloadAttemptException>();

            for (var position = 0; position < activeCandidateIndexes.Count; position++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidateIndex = activeCandidateIndexes[position];
                var resolution = candidates[candidateIndex];
                lastResolution = resolution;

                if (round == 1
                    && !resolution.ResolvedSourceKind.StartsWith("BmclApi", StringComparison.Ordinal)
                    && hostHealthTracker.ShouldAvoid(resolution.ResolvedSourceKind, GetCandidateHost(resolution)))
                {
                    var avoided = new DownloadAttemptException(
                        DownloadFailureDisposition.RetryCurrentSource,
                        DownloadFailureReason.Network,
                        "The download host is temporarily avoided after repeated transient failures.");
                    failures.Add(avoided);
                    var retryInLaterRound = round < maxRounds;
                    if (retryInLaterRound)
                    {
                        nextRoundCandidateIndexes.Add(candidateIndex);
                        nextRoundFailures.Add(avoided);
                    }
                    LogFailure(
                        resolution,
                        round,
                        avoided,
                        retryCurrentSource: false,
                        sourceRound: round,
                        retryInLaterRound: retryInLaterRound,
                        remainingCandidateCount: activeCandidateIndexes.Count - position - 1);
                    continue;
                }

                try
                {
                    var value = await ExecuteAttemptAsync(
                        resolution,
                        round,
                        operation,
                        noResultStatus: null,
                        cancellationToken: cancellationToken,
                        configureRequest: configureRequest,
                        allowResponseStatus: allowResponseStatus,
                        sensitiveHeaders: sensitiveHeaders,
                        speedMeter: speedMeter,
                        enableSlowBodyWatchdog: true).ConfigureAwait(false);
                    hostHealthTracker.RecordSuccess(resolution.ResolvedSourceKind, GetCandidateHost(resolution));
                    LogResolvedRequest(resolution, round);
                    return DownloadLookupResult<T>.Success(value);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    var failure = ClassifyException(exception);
                    RecordHostResult(resolution, failure);
                    hostHealthTracker.RecordFailure(
                        resolution.ResolvedSourceKind,
                        failure.FinalHost ?? GetCandidateHost(resolution),
                        failure.Reason,
                        failure.StatusCode);
                    if (failure.Disposition is DownloadFailureDisposition.Abort)
                        throw failure;

                    failures.Add(failure);
                    var retryInLaterRound = failure.Disposition is DownloadFailureDisposition.RetryCurrentSource
                        && round < maxRounds;
                    if (retryInLaterRound)
                    {
                        nextRoundCandidateIndexes.Add(candidateIndex);
                        nextRoundFailures.Add(failure);
                    }

                    LogFailure(
                        resolution,
                        round,
                        failure,
                        retryCurrentSource: false,
                        sourceRound: round,
                        retryInLaterRound: retryInLaterRound,
                        remainingCandidateCount: activeCandidateIndexes.Count - position - 1);
                }
            }

            if (round >= maxRounds || nextRoundCandidateIndexes.Count == 0)
                break;

            var retryDelay = nextRoundFailures
                .Select(failure => GetRetryDelay(failure, round))
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();
            logger.LogInformation(
                "File download source round exhausted. OriginalUrl={OriginalUrl} CompletedRound={CompletedRound} MaxRounds={MaxRounds} RetryCandidateCount={RetryCandidateCount} RetryDelay={RetryDelay}",
                DownloadUriLogSanitizer.Sanitize(originalUrl),
                round,
                maxRounds,
                nextRoundCandidateIndexes.Count,
                retryDelay);
            if (retryDelay > TimeSpan.Zero)
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            activeCandidateIndexes = nextRoundCandidateIndexes;
        }

        var finalResolution = lastResolution
            ?? throw new InvalidOperationException($"No download candidates were available for {originalUrl}.");
        var finalException = failures.LastOrDefault()
            ?? new InvalidOperationException($"No download source returned a usable result for {originalUrl}.");
        throw new DownloadSourceRequestException(finalResolution, finalException, failures);
    }

    private static IReadOnlyList<ResolvedDownloadRequest> ResolveFileCandidates(
        IReadOnlyList<string> originalUrls,
        DownloadSourcePreference preference,
        string? categoryHint)
    {
        ArgumentNullException.ThrowIfNull(originalUrls);
        var candidates = new List<ResolvedDownloadRequest>();
        var seenActualUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var originalUrl in originalUrls.Where(url => !string.IsNullOrWhiteSpace(url)))
        {
            foreach (var candidate in MinecraftDownloadSourceResolver.EnumerateRequests(originalUrl, preference, categoryHint))
            {
                if (seenActualUrls.Add(candidate.ActualUrl))
                    candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
            throw new ArgumentException("At least one file download URL is required.", nameof(originalUrls));
        return candidates;
    }

    /// <summary>
    /// 在并发租约内完成单次 HTTP 响应和调用方操作，并把状态码转换为统一失败类型。
    /// </summary>
    private async Task<T> ExecuteAttemptAsync<T>(
        ResolvedDownloadRequest resolution,
        int attempt,
        Func<DownloadAttemptContext, CancellationToken, Task<T>> operation,
        Func<HttpStatusCode, bool>? noResultStatus,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage, ResolvedDownloadRequest>? configureRequest,
        Func<HttpStatusCode, bool>? allowResponseStatus,
        DownloadRequestHeaders? sensitiveHeaders,
        SpeedMeter? speedMeter,
        bool enableSlowBodyWatchdog)
    {
        // 租约覆盖响应处理，而不只是发送请求，防止大量响应体同时占用网络与磁盘。
        await using var transportResult = await transport.SendAsync(
            resolution.ActualUrl,
            cancellationToken,
            configureRequest is null ? null : request => configureRequest(request, resolution),
            sensitiveHeaders,
            applyColdStartJitter: attempt == 1).ConfigureAwait(false);
        using var response = transportResult.Response;
        var globalConcurrency = GetGlobalConcurrencySnapshot();
        var hostConcurrency = transportResult.HostSnapshot;

        if (!string.Equals(GetCandidateHost(resolution), transportResult.FinalHost, StringComparison.OrdinalIgnoreCase)
            && hostHealthTracker.ShouldAvoid(resolution.ResolvedSourceKind, transportResult.FinalHost))
        {
            throw new DownloadAttemptException(
                DownloadFailureDisposition.SwitchSource,
                DownloadFailureReason.Network,
                "The redirected download host is temporarily avoided after a degraded transfer.")
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }

        if (noResultStatus?.Invoke(response.StatusCode) is true)
        {
            throw new DownloadNoResultException(
                $"The source returned HTTP {(int)response.StatusCode} for a lookup request.");
        }

        if (!response.IsSuccessStatusCode && allowResponseStatus?.Invoke(response.StatusCode) is not true)
        {
            throw CreateStatusFailure(response)
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }

        var telemetry = new DownloadAttemptTelemetry();
        try
        {
        telemetry.StartBody();
        await DownloadResponseThrottler.ApplyAsync(
            response,
            bandwidthLimiter,
            cancellationToken,
            bodyIdleTimeout: retryOptions.BodyIdleTimeout,
            firstByteTimeout: retryOptions.FirstByteTimeout,
            slowBodyReadThreshold: enableSlowBodyWatchdog ? retryOptions.SlowBodyReadThreshold : null,
            minimumBodyBytesPerSecond: retryOptions.MinimumBodyBytesPerSecond,
            timeProvider: timeProvider,
            reportBodyBytes: bytes =>
            {
                telemetry.ReportBodyBytes(bytes);
            },
            speedMeter: speedMeter).ConfigureAwait(false);
            var value = await operation(
                new DownloadAttemptContext(resolution, response, transportResult, attempt),
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Download transport completed. RequestedSourcePreference={RequestedSourcePreference} ResolvedSourceKind={ResolvedSourceKind} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} FinalHost={FinalHost} RedirectCount={RedirectCount} Attempt={Attempt} StatusCode={StatusCode} ResponseHeadersDuration={ResponseHeadersDuration} ResponseBodyDuration={ResponseBodyDuration} ContentLength={ContentLength} DownloadedBytes={DownloadedBytes} AverageBytesPerSecond={AverageBytesPerSecond} GlobalActive={GlobalActive} GlobalWaiting={GlobalWaiting} GlobalTarget={GlobalTarget} HostOrigin={HostOrigin} HostActive={HostActive} HostWaiting={HostWaiting} HostTarget={HostTarget}",
                resolution.RequestedSourcePreference,
                resolution.ResolvedSourceKind,
                DownloadUriLogSanitizer.Sanitize(transportResult.OriginalUri),
                DownloadUriLogSanitizer.Sanitize(transportResult.FinalUri),
                transportResult.FinalHost,
                transportResult.RedirectCount,
                attempt,
                (int)response.StatusCode,
                transportResult.ResponseHeadersDuration,
                telemetry.BodyDuration,
                response.Content.Headers.ContentLength,
                telemetry.BodyBytes,
                telemetry.AverageBytesPerSecond,
                globalConcurrency.ActiveCount,
                globalConcurrency.WaitingCount,
                globalConcurrency.CurrentTarget,
                transportResult.AdmissionOrigin,
                hostConcurrency?.ActiveCount ?? 0,
                hostConcurrency?.WaitingCount ?? 0,
                hostConcurrency?.CurrentTarget ?? 0);
            RecordHostResult(transportResult.AdmissionOrigin, failureReason: null, statusCode: null);
            hostHealthTracker.RecordSuccess(resolution.ResolvedSourceKind, transportResult.FinalHost);
            return value;
        }
        catch (DownloadNoResultException)
        {
            throw;
        }
        catch (DownloadAttemptException exception)
        {
            var slowFailure = exception as DownloadBodyTooSlowException;
            logger.LogWarning(
                exception,
                "Download transport failed after response headers. FinalHost={FinalHost} Attempt={Attempt} FailureReason={FailureReason} ResponseHeadersDuration={ResponseHeadersDuration} ResponseBodyDuration={ResponseBodyDuration} DownloadedBytes={DownloadedBytes} AverageBytesPerSecond={AverageBytesPerSecond} SlowReadDuration={SlowReadDuration} SlowReadBytes={SlowReadBytes} SlowReadBytesPerSecond={SlowReadBytesPerSecond} GlobalActive={GlobalActive} GlobalWaiting={GlobalWaiting} GlobalTarget={GlobalTarget} HostOrigin={HostOrigin} HostActive={HostActive} HostWaiting={HostWaiting} HostTarget={HostTarget}",
                transportResult.FinalHost,
                attempt,
                exception.Reason,
                transportResult.ResponseHeadersDuration,
                telemetry.BodyDuration,
                telemetry.BodyBytes,
                telemetry.AverageBytesPerSecond,
                slowFailure?.ReadDuration,
                slowFailure?.BytesRead,
                slowFailure?.BytesPerSecond,
                globalConcurrency.ActiveCount,
                globalConcurrency.WaitingCount,
                globalConcurrency.CurrentTarget,
                transportResult.AdmissionOrigin,
                hostConcurrency?.ActiveCount ?? 0,
                hostConcurrency?.WaitingCount ?? 0,
                hostConcurrency?.CurrentTarget ?? 0);
            throw exception
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new DownloadBodyInterruptedException("The response body read timed out.", exception)
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }
        catch (JsonException exception)
        {
            throw new DownloadContentValidationException("The response body is not valid JSON.", exception)
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }
        catch (XmlException exception)
        {
            throw new DownloadContentValidationException("The response body is not valid XML.", exception)
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }
        catch (HttpRequestException exception)
        {
            throw new DownloadBodyInterruptedException("The response body network read failed.", exception)
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }
        catch (IOException exception)
        {
            throw new DownloadBodyInterruptedException("The response body ended unexpectedly.", exception)
                .WithFinalHost(transportResult.FinalHost)
                .WithFinalOrigin(transportResult.AdmissionOrigin);
        }
    }

    private static DownloadAttemptException CreateStatusFailure(HttpResponseMessage response)
    {
        var statusCode = response.StatusCode;
        var value = (int)statusCode;
        var retry = statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || value is >= 500 and <= 599
            || value is < 400 or > 499;
        return new DownloadAttemptException(
            retry
                ? DownloadFailureDisposition.RetryCurrentSource
                : DownloadFailureDisposition.SwitchSource,
            DownloadFailureReason.HttpStatus,
            $"The source returned HTTP {value} ({statusCode}).",
            statusCode: statusCode,
            retryAfter: statusCode == HttpStatusCode.TooManyRequests ? ParseRetryAfter(response) : null);
    }

    internal TimeSpan GetRetryDelay(DownloadAttemptException failure, int attempt)
    {
        if (failure.StatusCode == HttpStatusCode.TooManyRequests && failure.RetryAfter.HasValue)
            return failure.RetryAfter.Value > retryOptions.MaximumRetryAfter
                ? retryOptions.MaximumRetryAfter
                : failure.RetryAfter.Value;

        var multiplier = Math.Pow(2, Math.Max(attempt - 1, 0));
        var milliseconds = Math.Min(
            retryOptions.RetryDelay.TotalMilliseconds * multiplier,
            retryOptions.MaximumRetryDelay.TotalMilliseconds);
        var jitterMilliseconds = 2000 * Math.Clamp(nextRetryJitter(), 0, 1);
        return TimeSpan.FromMilliseconds(Math.Min(
            retryOptions.MaximumRetryDelay.TotalMilliseconds,
            milliseconds + jitterMilliseconds));
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>
    /// 将网络、响应体、本地文件和内容校验异常分类为终止、重试当前源或切换源。
    /// </summary>
    private static DownloadAttemptException ClassifyException(Exception exception)
    {
        if (exception is DownloadAttemptException downloadAttemptException)
            return downloadAttemptException;

        if (exception is JsonException or XmlException)
        {
            return new DownloadContentValidationException(
                "The response content could not be parsed.",
                exception);
        }

        if (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            return new DownloadAttemptException(
                DownloadFailureDisposition.RetryCurrentSource,
                DownloadFailureReason.Network,
                "The download attempt failed due to a transient network error.",
                exception);
        }

        return new DownloadAttemptException(
            DownloadFailureDisposition.Abort,
            DownloadFailureReason.LocalFileSystem,
            "The download attempt failed due to a non-network error.",
            exception);
    }

    private ValueTask<IImportConcurrencyLease> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        return category switch
        {
            DownloadConcurrencyCategory.Metadata => limiter.AcquireMetadataSlotAsync(cancellationToken),
            DownloadConcurrencyCategory.Modpack => limiter.AcquireModpackDownloadSlotAsync(cancellationToken),
            DownloadConcurrencyCategory.Runtime => limiter.AcquireRuntimeDownloadSlotAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported download concurrency category.")
        };
    }

    private ValueTask<DownloadHostConcurrencyController.DownloadAdmissionLease> AcquireAdmissionAsync(
        Uri uri,
        bool applyColdStartJitter,
        CancellationToken cancellationToken)
    {
        return hostConcurrencyController.AcquireAsync(
            uri,
            token => AcquireRateLimitedLeaseAsync(uri, token),
            applyColdStartJitter,
            cancellationToken);
    }

    private async ValueTask<IImportConcurrencyLease> AcquireRateLimitedLeaseAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        await bmclApiRequestRateLimiter.WaitAsync(uri, cancellationToken).ConfigureAwait(false);
        return await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RecordHostResult(
        ResolvedDownloadRequest resolution,
        DownloadAttemptException failure)
    {
        var origin = failure.FinalOrigin;
        if (string.IsNullOrWhiteSpace(origin)
            && Uri.TryCreate(resolution.ActualUrl, UriKind.Absolute, out var uri))
        {
            origin = DownloadHostConcurrencyController.NormalizeOrigin(uri);
        }
        RecordHostResult(origin, failure.Reason, failure.StatusCode);
    }

    private void RecordHostResult(
        string? origin,
        DownloadFailureReason? failureReason,
        HttpStatusCode? statusCode)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return;
        var adjustment = hostConcurrencyController.RecordResult(origin, failureReason, statusCode);
        if (adjustment is null)
            return;
        logger.LogInformation(
            "Download host concurrency adjusted. HostOrigin={HostOrigin} PreviousTarget={PreviousTarget} CurrentTarget={CurrentTarget} AdjustmentReason={AdjustmentReason} SuccessCount={SuccessCount} FailureCount={FailureCount}",
            adjustment.Origin,
            adjustment.PreviousTarget,
            adjustment.CurrentTarget,
            adjustment.Reason,
            adjustment.Successes,
            adjustment.Failures);
    }

    private (int ActiveCount, int WaitingCount, int CurrentTarget) GetGlobalConcurrencySnapshot()
    {
        if (limiter is ImportConcurrencyLimiter importLimiter)
        {
            var snapshot = importLimiter.DownloadSnapshot;
            return (snapshot.ActiveCount, snapshot.WaitingCount, snapshot.CurrentTarget);
        }

        return (0, 0, 0);
    }

    private sealed class DownloadAttemptTelemetry
    {
        private readonly Stopwatch bodyStopwatch = new();
        private long bodyBytes;

        public long BodyBytes => Interlocked.Read(ref bodyBytes);
        public TimeSpan BodyDuration => bodyStopwatch.Elapsed;
        public double AverageBytesPerSecond => BodyDuration.TotalSeconds <= 0
            ? 0
            : BodyBytes / BodyDuration.TotalSeconds;

        public void StartBody() => bodyStopwatch.Start();
        public void ReportBodyBytes(long bytes) => Interlocked.Add(ref bodyBytes, bytes);
    }

    private static string GetCandidateHost(ResolvedDownloadRequest resolution) =>
        Uri.TryCreate(resolution.ActualUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    private void LogResolvedRequest(ResolvedDownloadRequest resolution, int attempt)
    {
        logger.LogInformation(
            "Download resource completed. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} Attempt={Attempt}",
            resolution.RequestedSourcePreference,
            resolution.ResourceCategory,
            DownloadUriLogSanitizer.Sanitize(resolution.OriginalUrl),
            DownloadUriLogSanitizer.Sanitize(resolution.ActualUrl),
            resolution.ResolvedSourceKind,
            attempt);
    }

    private void LogFailure(
        ResolvedDownloadRequest resolution,
        int attempt,
        DownloadAttemptException failure,
        bool retryCurrentSource,
        int? sourceRound = null,
        bool retryInLaterRound = false,
        int remainingCandidateCount = 0)
    {
        var slowFailure = failure as DownloadBodyTooSlowException;
        logger.LogWarning(
            failure,
            "Download resource attempt failed. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} FinalHost={FinalHost} Attempt={Attempt} SourceRound={SourceRound} FailureReason={FailureReason} FailureDisposition={FailureDisposition} StatusCode={StatusCode} RetryCurrentSource={RetryCurrentSource} RetryInLaterRound={RetryInLaterRound} RemainingCandidateCount={RemainingCandidateCount} SlowReadDuration={SlowReadDuration} SlowReadBytes={SlowReadBytes} SlowReadBytesPerSecond={SlowReadBytesPerSecond}",
            resolution.RequestedSourcePreference,
            resolution.ResourceCategory,
            DownloadUriLogSanitizer.Sanitize(resolution.OriginalUrl),
            DownloadUriLogSanitizer.Sanitize(resolution.ActualUrl),
            resolution.ResolvedSourceKind,
            failure.FinalHost,
            attempt,
            sourceRound,
            failure.Reason,
            failure.Disposition,
            failure.StatusCode is null ? null : (int)failure.StatusCode.Value,
            retryCurrentSource,
            retryInLaterRound,
            remainingCandidateCount,
            slowFailure?.ReadDuration,
            slowFailure?.BytesRead,
            slowFailure?.BytesPerSecond);
    }

    private enum DownloadExecutionMode
    {
        PerSourceRetries,
        FileSourceRounds
    }

    internal sealed class DownloadSourceRequestException : Exception
    {
        public DownloadSourceRequestException(
            ResolvedDownloadRequest resolution,
            Exception innerException,
            IReadOnlyList<Exception>? failures = null)
            : base($"Download request failed for {resolution.ActualUrl}", innerException)
        {
            Resolution = resolution;
            Failures = failures ?? [innerException];
        }

        public ResolvedDownloadRequest Resolution { get; }
        public IReadOnlyList<Exception> Failures { get; }
    }

}
