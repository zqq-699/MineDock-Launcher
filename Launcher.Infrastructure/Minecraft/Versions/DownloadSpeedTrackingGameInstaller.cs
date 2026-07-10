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
    private readonly DownloadSpeedAggregator speedAggregator;
    private readonly MinecraftDownloadRequestExecutor downloadExecutor;
    private readonly DownloadSourcePreference downloadSourcePreference;

    private DownloadSpeedTrackingGameInstaller(
        int maxChecker,
        int maxDownloader,
        int boundedCapacity,
        HttpClient httpClient,
        MinecraftDownloadRequestExecutor downloadExecutor,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress)
        : base(maxChecker, maxDownloader, boundedCapacity, httpClient)
    {
        this.downloadExecutor = downloadExecutor;
        this.downloadSourcePreference = downloadSourcePreference;
        speedAggregator = new DownloadSpeedAggregator(progress);
    }

    public static DownloadSpeedTrackingGameInstaller CreateAsCoreCount(
        HttpClient httpClient,
        MinecraftDownloadRequestExecutor downloadExecutor,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress)
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
            progress);
    }

    protected override Task Download(
        GameFile file,
        IProgress<ByteProgress>? progress,
        CancellationToken cancellationToken)
    {
        return DownloadGameFileAsync(file, progress, cancellationToken);
    }

    internal Task DownloadGameFileAsync(
        GameFile file,
        IProgress<ByteProgress>? progress,
        CancellationToken cancellationToken)
    {
        speedAggregator.ReportDownloadStarted();
        var fileUrl = file.Url
            ?? throw new InvalidDataException($"CmlLib game file URL is missing: {file.Name}");
        var filePath = file.Path
            ?? throw new InvalidDataException($"CmlLib game file path is missing: {file.Name}");
        NetworkDownloadProgress? attemptProgress = null;
        var currentAttempt = 0;
        return downloadExecutor.DownloadFileAsync(
            fileUrl,
            downloadSourcePreference,
            categoryHint: null,
            filePath,
            file.Hash,
            file.Size > 0 ? file.Size : null,
            reportDownloadedBytes: null,
            cancellationToken,
            (attempt, progressedBytes, totalBytes) =>
            {
                if (attempt != currentAttempt)
                {
                    currentAttempt = attempt;
                    attemptProgress = new NetworkDownloadProgress(progress, speedAggregator);
                }

                attemptProgress!.Report(new ByteProgress(
                    progressedBytes,
                    totalBytes.GetValueOrDefault(file.Size)));
            });
    }

    private sealed class NetworkDownloadProgress : IProgress<ByteProgress>
    {
        private static readonly TimeSpan InnerProgressInterval = TimeSpan.FromMilliseconds(100);

        private readonly IProgress<ByteProgress>? innerProgress;
        private readonly DownloadSpeedAggregator speedAggregator;
        private long lastProgressedBytes;
        private long lastReportedProgressedBytes;
        private DateTimeOffset lastInnerProgressReportedAt = DateTimeOffset.MinValue;

        public NetworkDownloadProgress(
            IProgress<ByteProgress>? innerProgress,
            DownloadSpeedAggregator speedAggregator)
        {
            this.innerProgress = innerProgress;
            this.speedAggregator = speedAggregator;
        }

        public void Report(ByteProgress value)
        {
            if (ShouldReportInnerProgress(value))
                innerProgress?.Report(value);

            var bytesDelta = value.ProgressedBytes - lastProgressedBytes;
            lastProgressedBytes = value.ProgressedBytes;
            if (bytesDelta > 0)
                speedAggregator.ReportDownloadedBytes(bytesDelta);
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

    private sealed class DownloadSpeedAggregator
    {
        private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(0.75);

        private readonly object syncRoot = new();
        private readonly IProgress<LauncherProgress>? progress;
        private long windowBytes;
        private bool hasReportedDownloadStarted;
        private DateTimeOffset windowStartedAt = DateTimeOffset.UtcNow;

        public DownloadSpeedAggregator(IProgress<LauncherProgress>? progress)
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
            if (progress is null)
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
