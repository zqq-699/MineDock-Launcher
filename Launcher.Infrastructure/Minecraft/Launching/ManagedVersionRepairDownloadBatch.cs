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

using System.Collections.Concurrent;
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
    private readonly SlidingWindowDownloadSpeedReporter speedReporter;

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
        speedReporter = new SlidingWindowDownloadSpeedReporter(progress);
    }

    public async Task DownloadAllAsync(
        IEnumerable<RepairDownloadRequest> downloads,
        CancellationToken cancellationToken)
    {
        var uniqueDownloads = downloads
            .Where(download => !string.IsNullOrWhiteSpace(download.OriginalUrl)
                && !string.IsNullOrWhiteSpace(download.DestinationPath))
            .GroupBy(download => Path.GetFullPath(download.DestinationPath), PathComparer)
            .Select(EnsureCompatibleDuplicate)
            .ToArray();
        if (uniqueDownloads.Length == 0)
            return;

        var operationContexts = new ConcurrentDictionary<string, MinecraftDownloadOperationContext>(PathComparer);
        try
        {
            await Parallel.ForEachAsync(
                uniqueDownloads,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrency,
                    CancellationToken = cancellationToken
                },
                (download, token) => new ValueTask(DownloadAsync(download, operationContexts, token))).ConfigureAwait(false);
        }
        finally
        {
            foreach (var context in operationContexts.Values)
                context.Dispose();
        }
    }

    public async Task DownloadAsync(
        RepairDownloadRequest download,
        CancellationToken cancellationToken)
    {
        var operationContexts = new ConcurrentDictionary<string, MinecraftDownloadOperationContext>(PathComparer);
        try
        {
            await DownloadAsync(download, operationContexts, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var context in operationContexts.Values)
                context.Dispose();
        }
    }

    private async Task DownloadAsync(
        RepairDownloadRequest download,
        ConcurrentDictionary<string, MinecraftDownloadOperationContext> operationContexts,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(download.DestinationPath)!);
            var executor = new MinecraftDownloadRequestExecutor(
                httpClient,
                logger,
                bandwidthLimiter ?? DownloadBandwidthLimiter.Create(speedLimitMbPerSecond, downloadSpeedLimitState),
                category: DownloadConcurrencyCategory.Runtime);
            using var speedSession = new DownloadActivitySpeedSession(speedReporter);
            var options = CreateDownloadOptions(download, operationContexts);
            await executor.DownloadFileAsync(
                download.OriginalUrl,
                sourcePreference,
                download.ResourceCategory,
                download.DestinationPath,
                download.ExpectedSha1,
                download.ExpectedSize,
                speedReporter.ReportNetworkBytes,
                cancellationToken,
                reportActivity: speedSession.Report,
                options: options).ConfigureAwait(false);
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

    private static RepairDownloadRequest EnsureCompatibleDuplicate(IGrouping<string, RepairDownloadRequest> group)
    {
        var first = group.First();
        if (group.Any(download => !string.Equals(download.ExpectedSha1, first.ExpectedSha1, StringComparison.OrdinalIgnoreCase)
            || download.ExpectedSize != first.ExpectedSize
            || download.PersistenceMode != first.PersistenceMode))
        {
            throw new InvalidDataException("Conflicting download identities resolved to the same destination path.");
        }
        return first;
    }

    private static DownloadFileOptions? CreateDownloadOptions(
        RepairDownloadRequest download,
        ConcurrentDictionary<string, MinecraftDownloadOperationContext> operationContexts)
    {
        if (download.PersistenceMode is not DownloadPersistenceMode.LightweightAtomic)
            return null;
        if (!MinecraftFileIntegrity.IsSha1(download.ExpectedSha1))
            throw new InvalidDataException("A lightweight asset download requires a SHA-1 value.");

        var managedRoot = Path.GetFullPath(download.ManagedRoot
            ?? Path.GetDirectoryName(download.DestinationPath)
            ?? throw new InvalidOperationException("Download destination has no managed root."));
        var context = operationContexts.GetOrAdd(managedRoot, static root => new MinecraftDownloadOperationContext(root));
        context.RegisterAsset(download.DestinationPath, download.ExpectedSha1!, download.ExpectedSize);
        return new DownloadFileOptions(DownloadPersistenceMode.LightweightAtomic, context);
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

}

internal sealed record RepairDownloadRequest(
    string OriginalUrl,
    string DestinationPath,
    string ResourceCategory,
    string? LibraryName,
    string? ArtifactPath,
    string? ExpectedSha1 = null,
    long? ExpectedSize = null,
    DownloadPersistenceMode PersistenceMode = DownloadPersistenceMode.TaskScopedResumable,
    string? ManagedRoot = null);
