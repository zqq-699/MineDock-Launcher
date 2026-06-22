using System.IO;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftSharedContentCopier
{
    public static MinecraftSharedContentCopyResult CopySharedRuntimeContent(
        string sourceGameDirectory,
        string destinationGameDirectory,
        ILogger? logger = null)
    {
        var librariesCopied = CopyLibraries(sourceGameDirectory, destinationGameDirectory);
        var assetIndexesCopied = CopyAssetsIndexes(sourceGameDirectory, destinationGameDirectory);
        var assetObjectsCopied = CopyAssetsObjects(sourceGameDirectory, destinationGameDirectory);
        var logConfigsCopied = CopyLogConfigs(sourceGameDirectory, destinationGameDirectory);

        logger?.LogDebug(
            "Copied shared Minecraft runtime content. Source={SourceGameDirectory} Destination={DestinationGameDirectory} LibrariesCopied={LibrariesCopied} AssetIndexesCopied={AssetIndexesCopied} AssetObjectsCopied={AssetObjectsCopied} LogConfigsCopied={LogConfigsCopied}",
            sourceGameDirectory,
            destinationGameDirectory,
            librariesCopied,
            assetIndexesCopied,
            assetObjectsCopied,
            logConfigsCopied);

        return new MinecraftSharedContentCopyResult(
            librariesCopied,
            assetIndexesCopied,
            assetObjectsCopied,
            logConfigsCopied);
    }

    public static int CopyLibraries(string sourceGameDirectory, string destinationGameDirectory)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "libraries"),
            Path.Combine(destinationGameDirectory, "libraries"));
    }

    public static int CopyAssetsIndexes(string sourceGameDirectory, string destinationGameDirectory)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "indexes"),
            Path.Combine(destinationGameDirectory, "assets", "indexes"));
    }

    public static int CopyAssetsObjects(string sourceGameDirectory, string destinationGameDirectory)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "objects"),
            Path.Combine(destinationGameDirectory, "assets", "objects"));
    }

    public static int CopyLogConfigs(string sourceGameDirectory, string destinationGameDirectory)
    {
        return CopyDirectoryContentIfMissing(
            Path.Combine(sourceGameDirectory, "assets", "log_configs"),
            Path.Combine(destinationGameDirectory, "assets", "log_configs"));
    }

    private static int CopyDirectoryContentIfMissing(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            return 0;

        var copiedFileCount = 0;
        foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (File.Exists(destinationPath))
                continue;

            var destinationFileDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFileDirectory))
                Directory.CreateDirectory(destinationFileDirectory);

            File.Copy(sourceFilePath, destinationPath, overwrite: false);
            copiedFileCount++;
        }

        return copiedFileCount;
    }
}

internal sealed record MinecraftSharedContentCopyResult(
    int LibrariesCopied,
    int AssetIndexesCopied,
    int AssetObjectsCopied,
    int LogConfigsCopied);
