using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalResourcePackService : ILocalResourcePackService
{
    private const string SupportedArchiveExtension = ".zip";
    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<LocalResourcePackService> logger;
    private readonly string iconCacheDirectory;

    public LocalResourcePackService(
        LauncherPathProvider? pathProvider = null,
        ILogger<LocalResourcePackService>? logger = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.logger = logger ?? NullLogger<LocalResourcePackService>.Instance;
        iconCacheDirectory = Path.Combine(this.pathProvider.DefaultDataDirectory, "cache", "resourcepacks", "icons");
    }

    public Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Task.Run<IReadOnlyList<LocalResourcePack>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resourcePacksDirectory = GetResourcePacksDirectory(instance);
                if (!Directory.Exists(resourcePacksDirectory))
                {
                    logger.LogInformation(
                        "No local resource packs directory found. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                        instance.Id,
                        resourcePacksDirectory);
                    return [];
                }

                var resourcePacks = Directory.EnumerateFiles(
                        resourcePacksDirectory,
                        $"*{SupportedArchiveExtension}",
                        SearchOption.TopDirectoryOnly)
                    .Select(ToLocalResourcePack)
                    .OrderByDescending(resourcePack => resourcePack.CreatedAt)
                    .ThenBy(resourcePack => resourcePack.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                logger.LogInformation(
                    "Local resource packs loaded. InstanceId={InstanceId} Count={ResourcePackCount}",
                    instance.Id,
                    resourcePacks.Length);
                return resourcePacks;
            },
            cancellationToken);
    }

    public Task<LocalResourcePackImportResult> ImportAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Task.Run(
            () => ImportCore(instance, archivePath, cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(LocalResourcePack resourcePack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourcePack);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(resourcePack.FullPath))
                {
                    logger.LogInformation(
                        "Skipping local resource pack delete because file does not exist. Path={Path}",
                        resourcePack.FullPath);
                    return;
                }

                File.Delete(resourcePack.FullPath);
                logger.LogInformation("Local resource pack deleted. Path={Path}", resourcePack.FullPath);
            },
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalResourcePack> resourcePacks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourcePacks);

        return Task.Run(
            async () =>
            {
                foreach (var resourcePack in resourcePacks.DistinctBy(resourcePack => resourcePack.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeleteAsync(resourcePack, cancellationToken);
                }
            },
            cancellationToken);
    }

    private LocalResourcePackImportResult ImportCore(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            logger.LogInformation(
                "Skipping local resource pack import because archive does not exist. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.FileNotFound);
        }

        if (!normalizedArchivePath.EndsWith(SupportedArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Skipping local resource pack import because archive type is unsupported. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnsupportedArchive);
        }

        logger.LogInformation(
            "Importing local resource pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
            instance.Id,
            normalizedArchivePath);

        try
        {
            var resourcePacksDirectory = GetResourcePacksDirectory(instance);
            Directory.CreateDirectory(resourcePacksDirectory);

            var targetPath = ResolveUniqueFilePath(resourcePacksDirectory, Path.GetFileName(normalizedArchivePath));
            File.Copy(normalizedArchivePath, targetPath, overwrite: false);

            var importedResourcePack = ToLocalResourcePack(targetPath);
            logger.LogInformation(
                "Local resource pack archive imported. InstanceId={InstanceId} ArchivePath={ArchivePath} ResourcePackPath={ResourcePackPath}",
                instance.Id,
                normalizedArchivePath,
                targetPath);
            return LocalResourcePackImportResult.Success(importedResourcePack);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local resource pack archive because a file operation failed. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local resource pack archive because access was denied. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected failure while importing local resource pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);
        }
    }

    private LocalResourcePack ToLocalResourcePack(string path)
    {
        var file = new FileInfo(path);
        return new LocalResourcePack
        {
            Name = Path.GetFileNameWithoutExtension(file.Name),
            FileName = file.Name,
            FullPath = file.FullName,
            IconSource = TryGetCachedIconSource(file),
            CreatedAt = new DateTimeOffset(file.CreationTimeUtc)
        };
    }

    private string? TryGetCachedIconSource(FileInfo archiveFile)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archiveFile.FullName);
            var iconEntry = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName.Replace('\\', '/'), "pack.png", StringComparison.OrdinalIgnoreCase));
            if (iconEntry is null)
                return null;

            Directory.CreateDirectory(iconCacheDirectory);
            var cachePath = GetCachePath(archiveFile, iconEntry.FullName);
            if (File.Exists(cachePath))
                return new Uri(cachePath).AbsoluteUri;

            using var iconStream = iconEntry.Open();
            var bitmap = LoadBitmap(iconStream);
            try
            {
                SavePng(bitmap, cachePath);
            }
            catch (IOException) when (File.Exists(cachePath))
            {
            }

            return new Uri(cachePath).AbsoluteUri;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Failed to cache local resource pack icon. ResourcePackPath={ResourcePackPath}",
                archiveFile.FullName);
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unexpected failure while caching local resource pack icon. ResourcePackPath={ResourcePackPath}",
                archiveFile.FullName);
            return null;
        }
    }

    private static BitmapSource LoadBitmap(Stream source)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;

        var decoder = BitmapDecoder.Create(
            buffer,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("Embedded resource pack icon contains no frames.");
        frame.Freeze();
        return frame;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private string GetCachePath(FileInfo archiveFile, string iconEntryName)
    {
        var hashInput = $"{archiveFile.FullName}|{archiveFile.Length}|{archiveFile.LastWriteTimeUtc.Ticks}|{iconEntryName}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(iconCacheDirectory, $"{hash}.png");
    }

    private static string ResolveUniqueFilePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var index = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            index++;
        }

        return candidate;
    }

    private static string GetResourcePacksDirectory(GameInstance instance)
    {
        return Path.Combine(instance.InstanceDirectory, "resourcepacks");
    }
}
