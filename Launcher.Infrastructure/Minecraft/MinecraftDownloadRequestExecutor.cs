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
    private readonly MinecraftDownloadTransport transport;
    private readonly ILogger logger;
    private readonly DownloadBandwidthLimiter? bandwidthLimiter;
    private readonly IImportConcurrencyLimiter limiter;
    private readonly DownloadConcurrencyCategory category;
    private readonly DownloadRetryOptions retryOptions;
    private readonly DownloadHostHealthTracker hostHealthTracker;
    private readonly DownloadHostConcurrencyController hostConcurrencyController;
    private readonly Func<double> nextRetryJitter;

    public MinecraftDownloadRequestExecutor(
        HttpClient httpClient,
        ILogger? logger = null,
        DownloadBandwidthLimiter? bandwidthLimiter = null,
        IImportConcurrencyLimiter? limiter = null,
        DownloadConcurrencyCategory category = DownloadConcurrencyCategory.Metadata,
        DownloadRetryOptions? retryOptions = null,
        DownloadHostHealthTracker? hostHealthTracker = null,
        DownloadHostConcurrencyController? hostConcurrencyController = null,
        Func<double>? nextRetryJitter = null)
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
        this.nextRetryJitter = nextRetryJitter ?? Random.Shared.NextDouble;
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
        SpeedMeter? speedMeter = null)
    {
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
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return result.Value!;
            }
        }

        return DownloadFileCoreAsync();

        async Task<ResolvedDownloadRequest> DownloadFileCoreAsync()
        {
            await using var session = await ResumableDownloadFileSession.AcquireAsync(
                destinationPath,
                expectedSha1,
                expectedSize,
                cancellationToken,
                options).ConfigureAwait(false);
            if (session.IsComplete)
                return MinecraftDownloadSourceResolver.EnumerateRequests(originalUrl, preference, categoryHint).First();

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
                        reportAttemptProgress,
                        token).ConfigureAwait(false);
                    return context.Resolution;
                },
                noResultStatus: null,
                lookupMode: false,
                configureRequest: (request, resolution) => session.ConfigureRequest(request, resolution),
                allowResponseStatus: status => status == HttpStatusCode.RequestedRangeNotSatisfiable,
                sensitiveHeaders: sensitiveHeaders,
                speedMeter: speedMeter,
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
        SpeedMeter? speedMeter = null)
    {
        return DownloadCoreAsync();

        async Task<ResolvedDownloadRequest> DownloadCoreAsync()
        {
            await using var session = await ResumableDownloadFileSession.AcquireAsync(
                destinationPath,
                integrity,
                cancellationToken,
                options).ConfigureAwait(false);
            if (session.IsComplete)
                return MinecraftDownloadSourceResolver.EnumerateRequests(originalUrl, preference, categoryHint).First();

            var result = await ExecuteCoreAsync(
                originalUrl,
                preference,
                categoryHint,
                async (context, token) =>
                {
                    await session.WriteAsync(context.Response, context.Resolution, context.AttemptNumber, reportAttemptProgress, token).ConfigureAwait(false);
                    return context.Resolution;
                },
                noResultStatus: null,
                lookupMode: false,
                configureRequest: (request, resolution) => session.ConfigureRequest(request, resolution),
                allowResponseStatus: status => status == HttpStatusCode.RequestedRangeNotSatisfiable,
                sensitiveHeaders,
                speedMeter: speedMeter,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Value!;
        }
    }

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
        CancellationToken cancellationToken)
    {
        // 候选顺序已经包含用户偏好和镜像回退策略；每个源内部重试耗尽后才切换下一源。
        var candidates = MinecraftDownloadSourceResolver
            .EnumerateRequests(originalUrl, preference, categoryHint)
            .ToList();
        var failures = new List<Exception>();
        var noResultSourceCount = 0;
        ResolvedDownloadRequest? lastResolution = null;

        foreach (var resolution in candidates)
        {
            lastResolution = resolution;
            var sourceReportedNoResult = false;

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
                        speedMeter).ConfigureAwait(false);
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
        SpeedMeter? speedMeter)
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

        if (resolution.RequestedSourcePreference is DownloadSourcePreference.Auto
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
            logger.LogWarning(
                exception,
                "Download transport failed after response headers. FinalHost={FinalHost} Attempt={Attempt} FailureReason={FailureReason} ResponseHeadersDuration={ResponseHeadersDuration} ResponseBodyDuration={ResponseBodyDuration} DownloadedBytes={DownloadedBytes} AverageBytesPerSecond={AverageBytesPerSecond} GlobalActive={GlobalActive} GlobalWaiting={GlobalWaiting} GlobalTarget={GlobalTarget} HostOrigin={HostOrigin} HostActive={HostActive} HostWaiting={HostWaiting} HostTarget={HostTarget}",
                transportResult.FinalHost,
                attempt,
                exception.Reason,
                transportResult.ResponseHeadersDuration,
                telemetry.BodyDuration,
                telemetry.BodyBytes,
                telemetry.AverageBytesPerSecond,
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
            AcquireLeaseAsync,
            applyColdStartJitter,
            cancellationToken);
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
        bool retryCurrentSource)
    {
        logger.LogWarning(
            failure,
            "Download resource attempt failed. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} FinalHost={FinalHost} Attempt={Attempt} FailureReason={FailureReason} FailureDisposition={FailureDisposition} StatusCode={StatusCode} RetryCurrentSource={RetryCurrentSource}",
            resolution.RequestedSourcePreference,
            resolution.ResourceCategory,
            DownloadUriLogSanitizer.Sanitize(resolution.OriginalUrl),
            DownloadUriLogSanitizer.Sanitize(resolution.ActualUrl),
            resolution.ResolvedSourceKind,
            failure.FinalHost,
            attempt,
            failure.Reason,
            failure.Disposition,
            failure.StatusCode is null ? null : (int)failure.StatusCode.Value,
            retryCurrentSource);
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
