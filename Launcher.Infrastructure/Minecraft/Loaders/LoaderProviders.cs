using System.Net.Http;
using CmlLib.Core;
using CmlLib.Core.ModLoaders.FabricMC;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed class VanillaLoaderProvider : ILoaderProvider
{
    public LoaderKind Kind => LoaderKind.Vanilla;
    public string DisplayName => "原版";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [new LoaderVersionInfo("原版")];
        return Task.FromResult(versions);
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress("Install", $"正在下载原版 {minecraftVersion}"));
        var launcher = CreateLauncher(gameDirectory, progress);
        AttachProgress(launcher, progress);
        await launcher.InstallAsync(minecraftVersion, cancellationToken);
        return await VanillaVersionIsolator.CreateIsolatedVersionAsync(
            minecraftVersion,
            isolatedVersionName,
            gameDirectory,
            cancellationToken);
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

                ReportProgress("Bytes", "正在下载游戏文件", CalculateTotalPercent());
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
    private readonly HttpClient httpClient;

    public FabricLoaderProvider(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public LoaderKind Kind => LoaderKind.Fabric;
    public string DisplayName => "Fabric";
    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        var installer = new FabricInstaller(httpClient);
        var loaders = await installer.GetLoaders(minecraftVersion);
        return loaders
            .Where(loader => !string.IsNullOrWhiteSpace(loader.Version))
            .Select(loader => new LoaderVersionInfo(loader.Version!, loader.Stable))
            .ToList();
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress("Install", $"正在安装 Fabric {minecraftVersion}"));
        var path = new MinecraftPath(gameDirectory);
        var installer = new FabricInstaller(httpClient);
        var versionName = string.IsNullOrWhiteSpace(loaderVersion)
            ? await installer.Install(minecraftVersion, path)
            : await installer.Install(minecraftVersion, loaderVersion, path);

        var launcher = VanillaLoaderProvider.CreateLauncher(path, progress);
        VanillaLoaderProvider.AttachProgress(launcher, progress);
        await launcher.InstallAsync(versionName, cancellationToken);
        return versionName;
    }
}

public sealed class PlaceholderLoaderProvider : ILoaderProvider
{
    public PlaceholderLoaderProvider(LoaderKind kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName;
    }

    public LoaderKind Kind { get; }
    public string DisplayName { get; }
    public bool IsImplemented => false;

    public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LoaderVersionInfo> versions = [];
        return Task.FromResult(versions);
    }

    public Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{DisplayName} 将在后续版本接入。");
    }
}
