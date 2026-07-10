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

using System.IO;
using System.Net;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class ManagedVersionRepairDownloadBatch
{
    private const int MaxConcurrency = 8;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;
    private readonly DownloadSourcePreference sourcePreference;
    private readonly int speedLimitMbPerSecond;
    private readonly DownloadBandwidthLimiter? bandwidthLimiter;
    private readonly DownloadSpeedReporter speedReporter;

    public ManagedVersionRepairDownloadBatch(
        HttpClient httpClient,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger logger,
        DownloadSourcePreference sourcePreference,
        int speedLimitMbPerSecond,
        IProgress<LauncherProgress>? progress)
    {
        this.httpClient = httpClient;
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger;
        this.sourcePreference = sourcePreference;
        this.speedLimitMbPerSecond = speedLimitMbPerSecond;
        bandwidthLimiter = DownloadBandwidthLimiter.Create(speedLimitMbPerSecond, downloadSpeedLimitState);
        speedReporter = new DownloadSpeedReporter(progress);
    }

    public async Task DownloadAllAsync(
        IEnumerable<RepairDownloadRequest> downloads,
        CancellationToken cancellationToken)
    {
        var uniqueDownloads = downloads
            .Where(download => !string.IsNullOrWhiteSpace(download.OriginalUrl)
                && !string.IsNullOrWhiteSpace(download.DestinationPath))
            .GroupBy(download => Path.GetFullPath(download.DestinationPath), PathComparer)
            .Select(group => group.First())
            .ToArray();
        if (uniqueDownloads.Length == 0)
            return;

        await Parallel.ForEachAsync(
            uniqueDownloads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrency,
                CancellationToken = cancellationToken
            },
            (download, token) => new ValueTask(DownloadAsync(download, token))).ConfigureAwait(false);
    }

    public async Task DownloadAsync(
        RepairDownloadRequest download,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(download.DestinationPath)!);
            speedReporter.ReportDownloadStarted();
            var executor = new MinecraftDownloadRequestExecutor(
                httpClient,
                logger,
                bandwidthLimiter ?? DownloadBandwidthLimiter.Create(speedLimitMbPerSecond, downloadSpeedLimitState),
                category: DownloadConcurrencyCategory.Runtime);
            await executor.DownloadFileAsync(
                download.OriginalUrl,
                sourcePreference,
                download.ResourceCategory,
                download.DestinationPath,
                download.ExpectedSha1,
                download.ExpectedSize,
                speedReporter.ReportDownloadedBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (MinecraftDownloadRequestExecutor.DownloadSourceRequestException exception)
        {
            var statusCode = exception.Failures
                .OfType<DownloadAttemptException>()
                .LastOrDefault(failure => failure.StatusCode is not null)?
                .StatusCode;
            throw CreateDownloadException(download, exception.Resolution, statusCode, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InstanceRepairException)
        {
            var resolution = MinecraftDownloadSourceResolver
                .EnumerateRequests(download.OriginalUrl, sourcePreference, download.ResourceCategory)
                .First();
            throw CreateDownloadException(download, resolution, statusCode: null, exception);
        }
    }

    private static InstanceRepairException CreateDownloadException(
        RepairDownloadRequest download,
        ResolvedDownloadRequest resolution,
        HttpStatusCode? statusCode,
        Exception? innerException)
    {
        var diagnostic = new LaunchDownloadDiagnostic(
            resolution.OriginalUrl,
            resolution.ActualUrl,
            download.DestinationPath,
            statusCode is null ? null : (int)statusCode.Value,
            download.LibraryName,
            download.ArtifactPath,
            resolution.RequestedSourcePreference.ToString(),
            resolution.ResolvedSourceKind,
            resolution.ResourceCategory);
        var statusText = statusCode is null
            ? "without an HTTP response"
            : $"with HTTP {(int)statusCode.Value} ({statusCode.Value})";
        var message = $"Failed to download launch file {statusText}.";
        return innerException is null
            ? new InstanceRepairException(message, diagnostic)
            : new InstanceRepairException(message, innerException, diagnostic);
    }

    private sealed class DownloadSpeedReporter
    {
        private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(0.75);
        private readonly object syncRoot = new();
        private readonly IProgress<LauncherProgress>? progress;
        private long windowBytes;
        private bool hasReportedDownloadStarted;
        private DateTimeOffset windowStartedAt = DateTimeOffset.UtcNow;

        public DownloadSpeedReporter(IProgress<LauncherProgress>? progress)
        {
            this.progress = progress;
        }

        public void ReportDownloadStarted()
        {
            if (progress is null)
                return;

            lock (syncRoot)
            {
                if (hasReportedDownloadStarted)
                    return;

                hasReportedDownloadStarted = true;
                windowBytes = 0;
                windowStartedAt = DateTimeOffset.UtcNow;
                progress.Report(new LauncherProgress(
                    LaunchProgressStages.DownloadSpeed,
                    string.Empty,
                    DownloadSpeedText: "0 B/s"));
            }
        }

        public void ReportDownloadedBytes(long bytesDelta)
        {
            if (progress is null || bytesDelta <= 0)
                return;

            lock (syncRoot)
            {
                windowBytes += bytesDelta;
                var now = DateTimeOffset.UtcNow;
                var elapsed = now - windowStartedAt;
                if (elapsed < SampleWindow)
                    return;

                var speedText = FormatSpeed(windowBytes / elapsed.TotalSeconds);
                windowBytes = 0;
                windowStartedAt = now;
                progress.Report(new LauncherProgress(
                    LaunchProgressStages.DownloadSpeed,
                    string.Empty,
                    DownloadSpeedText: speedText));
            }
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024)
                return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";

            if (bytesPerSecond >= 1024)
                return $"{bytesPerSecond / 1024:0.0} KB/s";

            return $"{bytesPerSecond:0} B/s";
        }
    }
}

internal sealed record RepairDownloadRequest(
    string OriginalUrl,
    string DestinationPath,
    string ResourceCategory,
    string? LibraryName,
    string? ArtifactPath,
    string? ExpectedSha1 = null,
    long? ExpectedSize = null);
