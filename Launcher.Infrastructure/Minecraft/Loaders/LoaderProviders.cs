using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed class VanillaLoaderProvider : ILoaderProvider
{
    private readonly HttpClient httpClient;

    public VanillaLoaderProvider(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public LoaderKind Kind => LoaderKind.Vanilla;
    public bool IsImplemented => true;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [new LoaderVersionInfo(nameof(LoaderKind.Vanilla))];
        return Task.FromResult(versions);
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));

        var finalVersionName = await VanillaVersionComposer.CreateFinalVersionAsync(
            httpClient,
            minecraftVersion,
            isolatedVersionName,
            gameDirectory,
            cancellationToken);

        var launcher = CreateLauncher(gameDirectory, progress);
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

    internal static MinecraftLauncher CreateLauncher(string gameDirectory, IProgress<LauncherProgress>? progress)
    {
        return CreateLauncher(new MinecraftPath(gameDirectory), progress);
    }

    internal static MinecraftLauncher CreateLauncher(MinecraftPath path, IProgress<LauncherProgress>? progress)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(path);
        parameters.GameInstaller = DownloadSpeedTrackingGameInstaller.CreateAsCoreCount(parameters.HttpClient, progress);
        return new MinecraftLauncher(parameters);
    }
}

public sealed class FabricLoaderProvider : ILoaderProvider
{
    private const string NoLoaderMessagePrefix = "Cannot find any loader for";
    private readonly HttpClient httpClient;

    public FabricLoaderProvider(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public LoaderKind Kind => LoaderKind.Fabric;
    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}",
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                return [];

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind is not JsonValueKind.Array)
                return [];

            return json.RootElement
                .EnumerateArray()
                .Select(ReadLoaderVersion)
                .Where(version => version is not null)
                .Select(version => version!)
                .ToList();
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return [];
        }
        catch (Exception exception) when (IsNoAvailableVersionException(exception, minecraftVersion))
        {
            return [];
        }
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
        var path = new MinecraftPath(gameDirectory);
        var selectedLoaderVersion = loaderVersion;

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            var availableLoaders = await GetLoaderVersionsAsync(minecraftVersion, cancellationToken);
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
            cancellationToken);

        var launcher = VanillaLoaderProvider.CreateLauncher(path, progress);
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

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [];
        return Task.FromResult(versions);
    }

    public Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{Kind} is not implemented yet.");
    }
}
