using System.IO;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ModpackGameInstaller : IModpackGameInstaller
{
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers;
    private readonly IVanillaSharedRuntimePreparer vanillaSharedRuntimePreparer;
    private readonly IFinalVersionInstaller finalVersionInstaller;
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly string tempRootDirectory;
    private readonly ILogger logger;

    public ModpackGameInstaller(
        IEnumerable<ILoaderProvider> providers,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<ModpackGameInstaller>? logger = null)
        : this(
            providers,
            new VanillaSharedRuntimePreparer(logger: logger),
            new FinalVersionInstaller(),
            httpClient: null,
            downloadSpeedLimitState,
            tempRootDirectory: null,
            logger)
    {
    }

    internal ModpackGameInstaller(
        IEnumerable<ILoaderProvider> providers,
        IVanillaSharedRuntimePreparer vanillaSharedRuntimePreparer,
        IFinalVersionInstaller finalVersionInstaller,
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        string? tempRootDirectory = null,
        ILogger? logger = null)
    {
        this.providers = providers.ToDictionary(provider => provider.Kind);
        this.vanillaSharedRuntimePreparer = vanillaSharedRuntimePreparer;
        this.finalVersionInstaller = finalVersionInstaller;
        this.httpClient = httpClient ?? new HttpClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.tempRootDirectory = string.IsNullOrWhiteSpace(tempRootDirectory) ? Path.GetTempPath() : tempRootDirectory;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task InstallMinecraftBaseAsync(
        string minecraftVersion,
        string gameDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        await vanillaSharedRuntimePreparer.PrepareAsync(
            minecraftVersion,
            gameDirectory,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
    }

    public Task<string> InstallLoaderAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        return loader switch
        {
            LoaderKind.Vanilla => InstallVanillaInSandboxAsync(
                minecraftVersion,
                gameDirectory,
                isolatedVersionName,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            LoaderKind.Fabric => InstallFabricInSandboxAsync(
                minecraftVersion,
                loaderVersion,
                gameDirectory,
                isolatedVersionName,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            LoaderKind.Quilt => InstallQuiltInSandboxAsync(
                minecraftVersion,
                loaderVersion,
                gameDirectory,
                isolatedVersionName,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            _ => InstallInstanceAsync(
                minecraftVersion,
                loader,
                loaderVersion,
                gameDirectory,
                isolatedVersionName,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond)
        };
    }

    public Task<string> InstallInstanceAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
            throw new NotSupportedException($"{loader} is not implemented yet.");

        return provider.InstallAsync(
            minecraftVersion,
            gameDirectory,
            isolatedVersionName,
            loaderVersion,
            progress,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
    }

    private Task<string> InstallVanillaInSandboxAsync(
        string minecraftVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        return InstallComposedVersionInSandboxAsync(
            "Vanilla",
            minecraftVersion,
            gameDirectory,
            isolatedVersionName,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            sandboxMinecraftDirectory => VanillaVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                isolatedVersionName,
                sandboxMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken));
    }

    private async Task<string> InstallFabricInSandboxAsync(
        string minecraftVersion,
        string? loaderVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            if (!providers.TryGetValue(LoaderKind.Fabric, out var provider))
                throw new InvalidOperationException("Fabric loader provider is not available.");

            selectedLoaderVersion = (await provider.GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false))
                .FirstOrDefault()?
                .Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Fabric loader version available for {minecraftVersion}.");

        return await InstallComposedVersionInSandboxAsync(
            "Fabric",
            minecraftVersion,
            gameDirectory,
            isolatedVersionName,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            sandboxMinecraftDirectory => FabricVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                selectedLoaderVersion,
                isolatedVersionName,
                sandboxMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
            cancellationToken)).ConfigureAwait(false);
    }

    private async Task<string> InstallQuiltInSandboxAsync(
        string minecraftVersion,
        string? loaderVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            if (!providers.TryGetValue(LoaderKind.Quilt, out var provider))
                throw new InvalidOperationException("Quilt loader provider is not available.");

            selectedLoaderVersion = (await provider.GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false))
                .FirstOrDefault()?
                .Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Quilt loader version available for {minecraftVersion}.");

        return await InstallComposedVersionInSandboxAsync(
            "Quilt",
            minecraftVersion,
            gameDirectory,
            isolatedVersionName,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            sandboxMinecraftDirectory => QuiltVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                selectedLoaderVersion,
                isolatedVersionName,
                sandboxMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken)).ConfigureAwait(false);
    }

    private async Task<string> InstallComposedVersionInSandboxAsync(
        string loaderName,
        string minecraftVersion,
        string targetMinecraftDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        Func<string, Task<string>> composeAsync)
    {
        var sessionDirectory = Path.Combine(tempRootDirectory, "launcher-modpack-version", Guid.NewGuid().ToString("N"));
        var sandboxMinecraftDirectory = Path.Combine(sessionDirectory, ".minecraft");
        Directory.CreateDirectory(sessionDirectory);

        logger.LogInformation(
            "Installing modpack loader version through sandbox. Loader={Loader} MinecraftVersion={MinecraftVersion} TargetMinecraftDirectory={TargetMinecraftDirectory} TargetVersionName={TargetVersionName} SessionDirectory={SessionDirectory}",
            loaderName,
            minecraftVersion,
            targetMinecraftDirectory,
            isolatedVersionName,
            sessionDirectory);

        try
        {
            var seededRuntimeCopy = MinecraftSharedContentCopier.CopySharedRuntimeContent(
                targetMinecraftDirectory,
                sandboxMinecraftDirectory,
                logger);
            var finalVersionName = await composeAsync(sandboxMinecraftDirectory).ConfigureAwait(false);

            await finalVersionInstaller.InstallAsync(
                sandboxMinecraftDirectory,
                finalVersionName,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false);

            MinecraftVersionDirectoryCopier.CopyVersionDirectory(
                sandboxMinecraftDirectory,
                targetMinecraftDirectory,
                finalVersionName,
                allowExistingDestination: true);
            var appliedRuntimeCopy = MinecraftSharedContentCopier.CopySharedRuntimeContent(
                sandboxMinecraftDirectory,
                targetMinecraftDirectory,
                logger);

            logger.LogInformation(
                "Installed modpack loader version through sandbox. Loader={Loader} MinecraftVersion={MinecraftVersion} FinalVersionName={FinalVersionName} SessionDirectory={SessionDirectory} SeededLibrariesCopied={SeededLibrariesCopied} SeededAssetIndexesCopied={SeededAssetIndexesCopied} SeededAssetObjectsCopied={SeededAssetObjectsCopied} SeededLogConfigsCopied={SeededLogConfigsCopied} AppliedLibrariesCopied={AppliedLibrariesCopied} AppliedAssetIndexesCopied={AppliedAssetIndexesCopied} AppliedAssetObjectsCopied={AppliedAssetObjectsCopied} AppliedLogConfigsCopied={AppliedLogConfigsCopied}",
                loaderName,
                minecraftVersion,
                finalVersionName,
                sessionDirectory,
                seededRuntimeCopy.LibrariesCopied,
                seededRuntimeCopy.AssetIndexesCopied,
                seededRuntimeCopy.AssetObjectsCopied,
                seededRuntimeCopy.LogConfigsCopied,
                appliedRuntimeCopy.LibrariesCopied,
                appliedRuntimeCopy.AssetIndexesCopied,
                appliedRuntimeCopy.AssetObjectsCopied,
                appliedRuntimeCopy.LogConfigsCopied);
            return finalVersionName;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Installing modpack loader version through sandbox failed. Loader={Loader} MinecraftVersion={MinecraftVersion} TargetVersionName={TargetVersionName} SessionDirectory={SessionDirectory}",
                loaderName,
                minecraftVersion,
                isolatedVersionName,
                sessionDirectory);
            throw;
        }
        finally
        {
            TryDeleteSandboxDirectory(sessionDirectory);
        }
    }

    private void TryDeleteSandboxDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete modpack loader sandbox directory. Directory={Directory}",
                directory);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete modpack loader sandbox directory. Directory={Directory}",
                directory);
        }
    }
}
