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

using System.Net.Http;
using System.IO;
using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadSpeedTrackingGameInstaller : ParallelGameInstaller
{
    private readonly SlidingWindowDownloadSpeedReporter speedReporter;
    private readonly MinecraftDownloadRequestExecutor downloadExecutor;
    private readonly DownloadSourcePreference downloadSourcePreference;
    private readonly MinecraftPath? minecraftPath;
    private readonly MinecraftDownloadOperationContext? operationContext;

    private DownloadSpeedTrackingGameInstaller(
        int maxChecker,
        int maxDownloader,
        int boundedCapacity,
        HttpClient httpClient,
        MinecraftDownloadRequestExecutor downloadExecutor,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        MinecraftPath? minecraftPath,
        MinecraftDownloadOperationContext? operationContext,
        SlidingWindowDownloadSpeedReporter? sharedSpeedReporter)
        : base(maxChecker, maxDownloader, boundedCapacity, httpClient)
    {
        this.downloadExecutor = downloadExecutor;
        this.downloadSourcePreference = downloadSourcePreference;
        this.minecraftPath = minecraftPath;
        this.operationContext = operationContext;
        speedReporter = sharedSpeedReporter ?? new SlidingWindowDownloadSpeedReporter(progress);
    }

    public static DownloadSpeedTrackingGameInstaller CreateAsCoreCount(
        HttpClient httpClient,
        MinecraftDownloadRequestExecutor downloadExecutor,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        MinecraftPath? minecraftPath = null,
        MinecraftDownloadOperationContext? operationContext = null,
        SlidingWindowDownloadSpeedReporter? sharedSpeedReporter = null)
    {
        var maxChecker = Environment.ProcessorCount;
        maxChecker = Math.Max(1, maxChecker);
        maxChecker = Math.Min(4, maxChecker);

        var maxDownloader = Environment.ProcessorCount;
        maxDownloader = Math.Max(4, maxDownloader);
        maxDownloader = Math.Min(8, maxDownloader);

        return new DownloadSpeedTrackingGameInstaller(
            maxChecker,
            maxDownloader,
            2048,
            httpClient,
            downloadExecutor,
            downloadSourcePreference,
            progress,
            minecraftPath,
            operationContext,
            sharedSpeedReporter);
    }

    internal bool HasActiveNetworkTransfers => speedReporter.HasActiveTransfers;

    protected override Task Download(
        GameFile file,
        IProgress<ByteProgress>? progress,
        CancellationToken cancellationToken)
    {
        return DownloadGameFileAsync(file, progress, cancellationToken);
    }

    internal async Task DownloadGameFileAsync(
        GameFile file,
        IProgress<ByteProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var speedSession = new DownloadActivitySpeedSession(speedReporter);
        var fileUrl = file.Url
                ?? throw new InvalidDataException($"CmlLib game file URL is missing: {file.Name}");
        var filePath = file.Path
                ?? throw new InvalidDataException($"CmlLib game file path is missing: {file.Name}");
        if (minecraftPath is not null)
            filePath = MinecraftPathGuard.EnsureInstallFilePath(minecraftPath, filePath);
        long? expectedSize = file.Size > 0 ? file.Size : null;
        var options = operationContext?.TryGetAsset(filePath, file.Hash, expectedSize) is true
            ? new DownloadFileOptions(DownloadPersistenceMode.LightweightAtomic, operationContext)
            : operationContext is not null && MinecraftFileIntegrity.IsSha1(file.Hash)
                ? new DownloadFileOptions(DownloadPersistenceMode.TaskScopedResumable, operationContext)
                : null;
        NetworkDownloadProgress? attemptProgress = null;
        var currentAttempt = 0;
        await downloadExecutor.DownloadFileAsync(
                fileUrl,
                downloadSourcePreference,
                categoryHint: null,
                filePath,
                file.Hash,
                expectedSize,
                reportDownloadedBytes: speedReporter.ReportNetworkBytes,
                cancellationToken,
                (attempt, progressedBytes, totalBytes) =>
                {
                    if (attempt != currentAttempt)
                    {
                        currentAttempt = attempt;
                        attemptProgress = new NetworkDownloadProgress(progress);
                    }

                    attemptProgress!.Report(new ByteProgress(
                        progressedBytes,
                        totalBytes.GetValueOrDefault(file.Size)));
                },
            reportActivity: speedSession.Report,
            options: options).ConfigureAwait(false);
    }

    private sealed class NetworkDownloadProgress : IProgress<ByteProgress>
    {
        private static readonly TimeSpan InnerProgressInterval = TimeSpan.FromMilliseconds(100);

        private readonly IProgress<ByteProgress>? innerProgress;
        private long lastReportedProgressedBytes;
        private DateTimeOffset lastInnerProgressReportedAt = DateTimeOffset.MinValue;

        public NetworkDownloadProgress(
            IProgress<ByteProgress>? innerProgress)
        {
            this.innerProgress = innerProgress;
        }

        public void Report(ByteProgress value)
        {
            if (ShouldReportInnerProgress(value))
                innerProgress?.Report(value);

        }

        private bool ShouldReportInnerProgress(ByteProgress value)
        {
            if (innerProgress is null)
                return false;

            var now = DateTimeOffset.UtcNow;
            if (value.TotalBytes > 0 && value.ProgressedBytes >= value.TotalBytes)
            {
                lastReportedProgressedBytes = value.ProgressedBytes;
                lastInnerProgressReportedAt = now;
                return true;
            }

            var bytesDelta = value.ProgressedBytes - lastReportedProgressedBytes;
            var minBytesDelta = value.TotalBytes <= 0
                ? 256 * 1024
                : Math.Max(64 * 1024, value.TotalBytes / 200);

            if (bytesDelta < minBytesDelta && now - lastInnerProgressReportedAt < InnerProgressInterval)
                return false;

            lastReportedProgressedBytes = value.ProgressedBytes;
            lastInnerProgressReportedAt = now;
            return true;
        }
    }

}
