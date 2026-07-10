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
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));

        var finalVersionName = await VanillaVersionComposer.CreateFinalVersionAsync(
            httpClient,
            minecraftVersion,
            isolatedVersionName,
            gameDirectory,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);

        var launcher = CreateLauncher(
            gameDirectory,
            progress,
            downloadSourcePreference,
            logger,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState);
        AttachProgress(launcher, progress);
        await launcher.InstallAsync(finalVersionName, cancellationToken);
        return finalVersionName;
    }

    internal static void AttachProgress(MinecraftLauncher launcher, IProgress<LauncherProgress>? progress)
    {
        if (progress is null)
            return;

        var syncRoot = new object();
        var totalTasks = 0;
        var progressedTasks = 0;
        var currentTaskFraction = 0d;
        var lastPercent = 0d;
        var lastReportedPercent = 0d;
        var lastReportedAt = DateTimeOffset.MinValue;
        var lastReportedMessage = string.Empty;

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

                ReportProgress("Files", $"{args.EventType}: {args.Name}", CalculateTotalPercent());
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

            var nextPercent = Math.Clamp(percent.Value, 0, 100);
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

    internal static MinecraftLauncher CreateLauncher(
        string gameDirectory,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        ILogger? logger = null,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null)
    {
        return CreateLauncher(
            new MinecraftPath(gameDirectory),
            progress,
            downloadSourcePreference,
            logger,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState);
    }

    internal static MinecraftLauncher CreateLauncher(
        MinecraftPath path,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        ILogger? logger = null,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null)
    {
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
        parameters.GameInstaller = DownloadSpeedTrackingGameInstaller.CreateAsCoreCount(
            parameters.HttpClient,
            runtimeExecutor,
            downloadSourcePreference,
            progress);
        return new MinecraftLauncher(parameters);
    }
}

public sealed class FabricLoaderProvider : ILoaderProvider
{
    private const string NoLoaderMessagePrefix = "Cannot find any loader for";
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;

    public FabricLoaderProvider(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<FabricLoaderProvider>? logger = null)
    {
        this.httpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger<FabricLoaderProvider>.Instance;
    }

    public LoaderKind Kind => LoaderKind.Fabric;
    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Metadata);
        var result = await executor.ExecuteLookupAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}",
            downloadSourcePreference,
            categoryHint: "Fabric",
            async (context, token) =>
            {
                await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (json.RootElement.ValueKind is not JsonValueKind.Array)
                {
                    throw new DownloadContentValidationException(
                        "Fabric loader metadata is not a JSON array.");
                }

                var entries = json.RootElement.EnumerateArray().ToList();
                if (entries.Count == 0)
                    throw new DownloadNoResultException("Fabric returned an empty loader list.");

                var versions = entries
                    .Select(ReadLoaderVersion)
                    .Where(version => version is not null)
                    .Select(version => version!)
                    .ToList();
                if (versions.Count == 0)
                {
                    throw new DownloadContentValidationException(
                        "Fabric loader metadata contains no valid loader entries.");
                }

                return (IReadOnlyList<LoaderVersionInfo>)versions;
            },
            statusCode => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            cancellationToken);
        return result.Found ? result.Value! : [];
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
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
        var path = new MinecraftPath(gameDirectory);
        var selectedLoaderVersion = loaderVersion;

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            var availableLoaders = await GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
            selectedLoaderVersion = availableLoaders.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Fabric loader version available for {minecraftVersion}.");

        var finalVersionName = await FabricVersionComposer.CreateFinalVersionAsync(
            httpClient,
            minecraftVersion,
            selectedLoaderVersion,
            isolatedVersionName,
            gameDirectory,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);

        var launcher = VanillaLoaderProvider.CreateLauncher(
            path,
            progress,
            downloadSourcePreference,
            logger,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState);
        VanillaLoaderProvider.AttachProgress(launcher, progress);
        await launcher.InstallAsync(finalVersionName, cancellationToken);
        return finalVersionName;
    }

    internal static bool IsNoAvailableVersionException(Exception exception, string minecraftVersion)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException httpRequestException
                && httpRequestException.StatusCode is HttpStatusCode.NotFound)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var message = current.Message;
            if (message.Contains(NoLoaderMessagePrefix, StringComparison.OrdinalIgnoreCase))
                return true;

            if (message.Contains("404", StringComparison.OrdinalIgnoreCase)
                && message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (message.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase)
                && message.Contains("no loader", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (message.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase)
                && (message.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("could not find", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("not support", StringComparison.OrdinalIgnoreCase))
                && (message.Contains("loader", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("version", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("game", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static LoaderVersionInfo? ReadLoaderVersion(JsonElement item)
    {
        if (!item.TryGetProperty("loader", out var loader)
            || loader.ValueKind is not JsonValueKind.Object
            || !loader.TryGetProperty("version", out var versionProperty)
            || versionProperty.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var version = versionProperty.GetString();
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var isStable = loader.TryGetProperty("stable", out var stableProperty)
            && stableProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
            && stableProperty.GetBoolean();

        return new LoaderVersionInfo(version, isStable);
    }
}

public sealed class PlaceholderLoaderProvider : ILoaderProvider
{
    public PlaceholderLoaderProvider(LoaderKind kind)
    {
        Kind = kind;
    }

    public LoaderKind Kind { get; }
    public bool IsImplemented => false;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [];
        return Task.FromResult(versions);
    }

    public Task<string> InstallAsync(
        string minecraftVersion,
        string gameDirectory,
        string isolatedVersionName,
        string? loaderVersion,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        throw new NotSupportedException($"{Kind} is not implemented yet.");
    }
}
