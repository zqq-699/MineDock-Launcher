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
using System.Text.Json;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 安装原版 Minecraft，并提供各 Loader 共用的 CmlLib 下载进度与启动器配置。
/// </summary>
public sealed class VanillaLoaderProvider : ILoaderProvider
{
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;

    public VanillaLoaderProvider(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<VanillaLoaderProvider>? logger = null)
    {
        this.httpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger<VanillaLoaderProvider>.Instance;
    }

    public LoaderKind Kind => LoaderKind.Vanilla;
    public bool IsImplemented => true;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [new LoaderVersionInfo(nameof(LoaderKind.Vanilla))];
        return Task.FromResult(versions);
    }

    public async Task<string> InstallAsync(
        string minecraftVersion,
        string gameDirectory,
        string isolatedVersionName,
        string? loaderVersion,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        // 先生成隔离版本元数据，再交给 CmlLib 补齐文件；返回值必须是实际可启动版本名。
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
        using var downloadOperation = CreateDownloadOperationContext(new MinecraftPath(gameDirectory));
        using var speedReporter = new SlidingWindowDownloadSpeedReporter(progress);

        var finalVersionName = await VanillaVersionComposer.CreateFinalVersionAsync(
            httpClient,
            minecraftVersion,
            isolatedVersionName,
            gameDirectory,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken,
            downloadOperation,
            speedReporter);

        var launcher = CreateLauncher(
            gameDirectory,
            progress,
            downloadSourcePreference,
            logger,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            downloadOperation,
            speedReporter);
        AttachProgress(launcher, progress);
        await launcher.InstallAsync(finalVersionName, cancellationToken);
        return finalVersionName;
    }

    internal static void AttachProgress(MinecraftLauncher launcher, IProgress<LauncherProgress>? progress)
    {
        if (progress is null)
            return;

        // 文件数和当前文件字节进度组合成总体百分比，并强制单调，避免 UI 进度条倒退。
        // 高频事件可能来自多个下载线程，因此统计与节流状态统一在此锁内更新。
        var syncRoot = new object();
        var totalTasks = 0;
        var progressedTasks = 0;
        var currentTaskFraction = 0d;
        var lastPercent = 0d;
        var lastReportedPercent = 0d;
        var lastReportedAt = DateTimeOffset.MinValue;
        var lastReportedMessage = string.Empty;
        var speedTrackingInstaller = launcher.GameInstaller as DownloadSpeedTrackingGameInstaller;

        launcher.FileProgressChanged += (_, args) =>
        {
            lock (syncRoot)
            {
                if (args.TotalTasks > 0)
                    totalTasks = args.TotalTasks;

                progressedTasks = totalTasks <= 0
                    ? Math.Max(args.ProgressedTasks, 0)
                    : Math.Clamp(args.ProgressedTasks, 0, totalTasks);
                currentTaskFraction = 0;

                // CML only distinguishes queued/done file events.  Treat the
                // non-body part as verification, but do not clear a live speed
                // sample while another parallel file is still reading a body.
                var stage = speedTrackingInstaller?.HasActiveNetworkTransfers == true
                    ? LaunchProgressStages.DownloadingFiles
                    : LaunchProgressStages.CheckingFiles;
                ReportProgress(stage, $"{args.EventType}: {args.Name}", CalculateTotalPercent());
            }
        };

        launcher.ByteProgressChanged += (_, args) =>
        {
            lock (syncRoot)
            {
                currentTaskFraction = args.TotalBytes <= 0
                    ? 0
                    : Math.Clamp(args.ProgressedBytes * 1d / args.TotalBytes, 0, 1);

                ReportProgress(LaunchProgressStages.DownloadingFiles, string.Empty, CalculateTotalPercent());
            }
        };

        double? CalculateTotalPercent()
        {
            if (totalTasks <= 0)
                return null;

            return (progressedTasks + currentTaskFraction) * 100d / totalTasks;
        }

        void ReportProgress(string stage, string message, double? percent, string? downloadSpeedText = null)
        {
            var now = DateTimeOffset.UtcNow;
            if (percent is null)
            {
                if (now - lastReportedAt < TimeSpan.FromMilliseconds(250)
                    && string.Equals(lastReportedMessage, message, StringComparison.Ordinal))
                {
                    return;
                }

                lastReportedAt = now;
                lastReportedMessage = message;
                progress.Report(new LauncherProgress(stage, message, DownloadSpeedText: downloadSpeedText));
                return;
            }

            // CML may finish its file-check pass before the remaining download,
            // publishing and installer work. Keep 100% exclusively for the
            // caller's successful completion signal.
            var nextPercent = Math.Clamp(percent.Value, 0, 99);
            if (nextPercent < lastPercent)
                nextPercent = lastPercent;

            lastPercent = nextPercent;
            if (nextPercent < 100
                && nextPercent - lastReportedPercent < 0.35
                && now - lastReportedAt < TimeSpan.FromMilliseconds(120))
            {
                return;
            }

            lastReportedPercent = nextPercent;
            lastReportedAt = now;
            lastReportedMessage = message;
            progress.Report(new LauncherProgress(stage, message, nextPercent, downloadSpeedText));
        }
    }

    internal static MinecraftDownloadOperationContext CreateDownloadOperationContext(MinecraftPath path) =>
        new(Path.GetDirectoryName(path.Assets)
            ?? throw new InvalidOperationException("Minecraft assets path has no managed root."));

    internal static MinecraftLauncher CreateLauncher(
        string gameDirectory,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        ILogger? logger = null,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        MinecraftDownloadOperationContext? operationContext = null,
        SlidingWindowDownloadSpeedReporter? sharedSpeedReporter = null)
    {
        return CreateLauncher(
            new MinecraftPath(gameDirectory),
            progress,
            downloadSourcePreference,
            logger,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            operationContext,
            sharedSpeedReporter);
    }

    internal static MinecraftLauncher CreateLauncher(
        MinecraftPath path,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        ILogger? logger = null,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        MinecraftDownloadOperationContext? operationContext = null,
        SlidingWindowDownloadSpeedReporter? sharedSpeedReporter = null)
    {
        // 统一注入镜像路由、限速和运行库下载器，保证 Vanilla/Fabric 使用相同下载策略。
        var parameters = MinecraftLauncherParameters.CreateDefault(path);
        var bandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
        parameters.HttpClient = new HttpClient(new DownloadSourceRoutingHttpMessageHandler(
            downloadSourcePreference,
            DownloadConcurrencyCategory.Metadata,
            MinecraftHttpClientFactory.CreateTransportHandler(),
            logger,
            bandwidthLimiter))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var runtimeHttpClient = MinecraftHttpClientFactory.CreateTransportClient();
        var runtimeExecutor = new MinecraftDownloadRequestExecutor(
            runtimeHttpClient,
            logger,
            bandwidthLimiter,
            category: DownloadConcurrencyCategory.Runtime);
        var assetIndexExecutor = new MinecraftDownloadRequestExecutor(
            runtimeHttpClient,
            logger,
            bandwidthLimiter,
            category: DownloadConcurrencyCategory.Metadata);
        parameters.GameInstaller = DownloadSpeedTrackingGameInstaller.CreateAsCoreCount(
            parameters.HttpClient,
            runtimeExecutor,
            downloadSourcePreference,
            progress,
            path,
            operationContext,
            sharedSpeedReporter);
        var defaultAssetExtractor = parameters.FileExtractors!
            .Select((extractor, index) => (extractor, index))
            .First(entry => entry.extractor is CmlLib.Core.FileExtractors.AssetFileExtractor);
        if (parameters.FileExtractors is not null)
        {
            parameters.FileExtractors.RemoveAt(defaultAssetExtractor.index);
            parameters.FileExtractors.Insert(
                defaultAssetExtractor.index,
                new SafeAssetFileExtractor(assetIndexExecutor, downloadSourcePreference, operationContext, sharedSpeedReporter));
        }
        return new MinecraftLauncher(parameters);
    }
}

/// <summary>
/// 查询并安装 Fabric Loader，并把官方元数据差异映射为稳定的可用版本语义。
/// </summary>
