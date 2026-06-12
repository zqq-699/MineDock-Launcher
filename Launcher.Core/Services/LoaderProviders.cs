using CmlLib.Core;
using CmlLib.Core.ModLoaders.FabricMC;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

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

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress("Install", $"正在下载原版 {minecraftVersion}"));
        var launcher = new MinecraftLauncher(new MinecraftPath(gameDirectory));
        AttachProgress(launcher, progress);
        await launcher.InstallAsync(minecraftVersion, cancellationToken);
        return minecraftVersion;
    }

    internal static void AttachProgress(MinecraftLauncher launcher, IProgress<LauncherProgress>? progress)
    {
        if (progress is null)
            return;

        launcher.FileProgressChanged += (_, args) =>
        {
            double? percent = args.TotalTasks <= 0 ? null : args.ProgressedTasks * 100d / args.TotalTasks;
            progress.Report(new LauncherProgress("Files", $"{args.EventType}: {args.Name}", percent));
        };

        launcher.ByteProgressChanged += (_, args) =>
        {
            double? percent = args.TotalBytes <= 0 ? null : args.ProgressedBytes * 100d / args.TotalBytes;
            progress.Report(new LauncherProgress("Bytes", "正在下载游戏文件", percent));
        };
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
            .Select(loader => new LoaderVersionInfo(loader.Version, loader.Stable))
            .ToList();
    }

    public async Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress("Install", $"正在安装 Fabric {minecraftVersion}"));
        var path = new MinecraftPath(gameDirectory);
        var installer = new FabricInstaller(httpClient);
        var versionName = string.IsNullOrWhiteSpace(loaderVersion)
            ? await installer.Install(minecraftVersion, path)
            : await installer.Install(minecraftVersion, loaderVersion, path);

        var launcher = new MinecraftLauncher(path);
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

    public Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{DisplayName} 将在后续版本接入。");
    }
}
