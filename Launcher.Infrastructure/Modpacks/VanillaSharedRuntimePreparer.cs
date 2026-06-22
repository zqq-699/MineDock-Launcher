using CmlLib.Core;
using System.IO;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

internal interface IVanillaSharedRuntimePreparer
{
    Task PrepareAsync(
        string minecraftVersion,
        string targetMinecraftDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);
}

internal interface IVanillaVersionInstaller
{
    Task InstallAsync(
        string minecraftVersion,
        string gameDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);
}

internal sealed class VanillaSharedRuntimePreparer : IVanillaSharedRuntimePreparer
{
    private readonly IVanillaVersionInstaller vanillaVersionInstaller;
    private readonly string tempRootDirectory;
    private readonly ILogger logger;

    public VanillaSharedRuntimePreparer(
        IVanillaVersionInstaller? vanillaVersionInstaller = null,
        string? tempRootDirectory = null,
        ILogger? logger = null)
    {
        this.vanillaVersionInstaller = vanillaVersionInstaller ?? new VanillaVersionInstaller();
        this.tempRootDirectory = string.IsNullOrWhiteSpace(tempRootDirectory) ? Path.GetTempPath() : tempRootDirectory;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task PrepareAsync(
        string minecraftVersion,
        string targetMinecraftDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var sessionDirectory = Path.Combine(tempRootDirectory, "launcher-vanilla-runtime", Guid.NewGuid().ToString("N"));
        var sandboxMinecraftDirectory = Path.Combine(sessionDirectory, ".minecraft");

        Directory.CreateDirectory(sessionDirectory);
        logger.LogInformation(
            "Preparing shared Minecraft runtime in sandbox. MinecraftVersion={MinecraftVersion} SessionDirectory={SessionDirectory}",
            minecraftVersion,
            sessionDirectory);

        try
        {
            await vanillaVersionInstaller.InstallAsync(
                minecraftVersion,
                sandboxMinecraftDirectory,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false);

            var copyResult = MinecraftSharedContentCopier.CopySharedRuntimeContent(
                sandboxMinecraftDirectory,
                targetMinecraftDirectory,
                logger);

            logger.LogInformation(
                "Prepared shared Minecraft runtime. MinecraftVersion={MinecraftVersion} SessionDirectory={SessionDirectory} LibrariesCopied={LibrariesCopied} AssetIndexesCopied={AssetIndexesCopied} AssetObjectsCopied={AssetObjectsCopied} LogConfigsCopied={LogConfigsCopied}",
                minecraftVersion,
                sessionDirectory,
                copyResult.LibrariesCopied,
                copyResult.AssetIndexesCopied,
                copyResult.AssetObjectsCopied,
                copyResult.LogConfigsCopied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Preparing shared Minecraft runtime failed. MinecraftVersion={MinecraftVersion} SessionDirectory={SessionDirectory}",
                minecraftVersion,
                sessionDirectory);
            throw;
        }
        finally
        {
            TryDeleteDirectory(sessionDirectory);
        }
    }

    private void TryDeleteDirectory(string directory)
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
                "Failed to delete temporary shared runtime sandbox. Directory={Directory}",
                directory);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete temporary shared runtime sandbox. Directory={Directory}",
                directory);
        }
    }

    private sealed class VanillaVersionInstaller : IVanillaVersionInstaller
    {
        public async Task InstallAsync(
            string minecraftVersion,
            string gameDirectory,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            var launcher = VanillaLoaderProvider.CreateLauncher(
                new MinecraftPath(gameDirectory),
                progress,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond);
            VanillaLoaderProvider.AttachProgress(launcher, progress);
            await launcher.InstallAsync(minecraftVersion, cancellationToken).ConfigureAwait(false);
        }
    }
}
