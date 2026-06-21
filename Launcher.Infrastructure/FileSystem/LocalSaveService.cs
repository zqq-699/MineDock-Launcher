using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalSaveService : ILocalSaveService
{
    private readonly ILogger<LocalSaveService> logger;

    public LocalSaveService(ILogger<LocalSaveService>? logger = null)
    {
        this.logger = logger ?? NullLogger<LocalSaveService>.Instance;
    }

    public Task<IReadOnlyList<LocalSave>> GetSavesAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Task.Run<IReadOnlyList<LocalSave>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var savesDirectory = GetSavesDirectory(instance);
                if (!Directory.Exists(savesDirectory))
                {
                    logger.LogInformation(
                        "No local saves directory found. InstanceId={InstanceId} SavesDirectory={SavesDirectory}",
                        instance.Id,
                        savesDirectory);
                    return [];
                }

                var saves = Directory.EnumerateDirectories(savesDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Select(ToLocalSave)
                    .OrderByDescending(save => save.CreatedAt)
                    .ThenBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                logger.LogInformation(
                    "Local saves loaded. InstanceId={InstanceId} Count={SaveCount}",
                    instance.Id,
                    saves.Length);
                return saves;
            },
            cancellationToken);
    }

    public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(save.FullPath))
                {
                    logger.LogInformation(
                        "Skipping local save delete because directory does not exist. Path={Path}",
                        save.FullPath);
                    return;
                }

                Directory.Delete(save.FullPath, recursive: true);
                logger.LogInformation("Local save deleted. Path={Path}", save.FullPath);
            },
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saves);

        return Task.Run(
            async () =>
            {
                foreach (var save in saves.DistinctBy(save => save.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeleteAsync(save, cancellationToken);
                }
            },
            cancellationToken);
    }

    private static LocalSave ToLocalSave(string path)
    {
        var directory = new DirectoryInfo(path);
        var iconPath = Path.Combine(directory.FullName, "icon.png");

        return new LocalSave
        {
            Name = directory.Name,
            DirectoryName = directory.Name,
            FullPath = directory.FullName,
            IconSource = File.Exists(iconPath) ? iconPath : null,
            CreatedAt = new DateTimeOffset(directory.CreationTimeUtc)
        };
    }

    private static string GetSavesDirectory(GameInstance instance)
    {
        return Path.Combine(instance.InstanceDirectory, "saves");
    }
}
