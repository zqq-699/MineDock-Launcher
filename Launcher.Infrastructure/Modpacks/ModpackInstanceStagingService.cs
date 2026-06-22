using System.IO;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ModpackInstanceStagingService : IModpackInstanceStagingService
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceRepository repository;
    private readonly IGameInstanceService instanceService;

    public ModpackInstanceStagingService(
        ISettingsService settingsService,
        IGameInstanceRepository repository,
        IGameInstanceService instanceService)
    {
        this.settingsService = settingsService;
        this.repository = repository;
        this.instanceService = instanceService;
    }

    public async Task<StagedModpackInstance> StageAsync(
        PreparedModpack preparedModpack,
        string resolvedInstanceName,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var stagingContentDirectory = Path.Combine(preparedModpack.WorkingDirectory, "instance-content");
        repository.CreateInstanceDirectories(stagingContentDirectory);

        var now = DateTimeOffset.UtcNow;
        return new StagedModpackInstance
        {
            ResolvedInstanceName = resolvedInstanceName,
            MinecraftDirectory = settings.MinecraftDirectory,
            StagingContentDirectory = stagingContentDirectory,
            Instance = new GameInstance
            {
                Name = resolvedInstanceName,
                MinecraftVersion = preparedModpack.MinecraftVersion,
                Loader = preparedModpack.Loader,
                LoaderVersion = preparedModpack.Loader == LoaderKind.Vanilla ? null : preparedModpack.LoaderVersion,
                VersionName = resolvedInstanceName,
                VersionType = string.Empty,
                InstanceDirectory = stagingContentDirectory,
                MemoryMb = settings.DefaultMemoryMb,
                CreatedAt = now,
                UpdatedAt = now
            }
        };
    }

    public async Task<GameInstance> FinalizeAsync(
        StagedModpackInstance stagedInstance,
        string finalVersionName,
        CancellationToken cancellationToken = default)
    {
        var finalDirectory = repository.GetVersionDirectory(stagedInstance.MinecraftDirectory, finalVersionName);
        repository.CreateInstanceDirectories(finalDirectory);
        CopyDirectoryContent(stagedInstance.StagingContentDirectory, finalDirectory);

        var hadDefaultInstance = await instanceService.GetDefaultInstanceAsync(cancellationToken).ConfigureAwait(false) is not null;
        var instance = stagedInstance.Instance;
        instance.VersionName = finalVersionName;
        instance.InstanceDirectory = finalDirectory;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        await instanceService.SaveInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        if (!hadDefaultInstance)
            await instanceService.SetDefaultInstanceAsync(instance.Id, cancellationToken).ConfigureAwait(false);

        return instance;
    }

    public async Task CleanupFailedImportAsync(
        StagedModpackInstance stagedInstance,
        string? finalVersionName,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        TryDeleteDirectory(stagedInstance.StagingContentDirectory);

        if (!string.IsNullOrWhiteSpace(finalVersionName))
            TryDeleteVersionDirectory(settings.MinecraftDirectory, finalVersionName);

        if (!string.IsNullOrWhiteSpace(stagedInstance.ResolvedInstanceName))
            TryDeleteVersionDirectory(settings.MinecraftDirectory, stagedInstance.ResolvedInstanceName);
    }

    private static void CopyDirectoryContent(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            return;

        foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourceFilePath, destinationPath, overwrite: true);
        }
    }

    private void TryDeleteVersionDirectory(string minecraftDirectory, string versionName)
    {
        try
        {
            repository.DeleteVersionDirectory(minecraftDirectory, versionName);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
