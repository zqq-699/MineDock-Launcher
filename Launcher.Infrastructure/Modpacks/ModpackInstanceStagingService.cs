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
    private readonly SemaphoreSlim stagingLock = new(1, 1);

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
        string preferredInstanceName,
        CancellationToken cancellationToken = default)
    {
        await stagingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var resolvedInstanceName = await ResolveUniqueInstanceNameAsync(
                preferredInstanceName,
                settings.MinecraftDirectory,
                cancellationToken).ConfigureAwait(false);
            var instanceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, resolvedInstanceName);
            repository.CreateInstanceDirectories(instanceDirectory);

            var now = DateTimeOffset.UtcNow;
            return new StagedModpackInstance
            {
                ResolvedInstanceName = resolvedInstanceName,
                MinecraftDirectory = settings.MinecraftDirectory,
                InstanceDirectory = instanceDirectory,
                Instance = new GameInstance
                {
                    Name = resolvedInstanceName,
                    MinecraftVersion = preparedModpack.MinecraftVersion,
                    Loader = preparedModpack.Loader,
                    LoaderVersion = preparedModpack.Loader == LoaderKind.Vanilla ? null : preparedModpack.LoaderVersion,
                    VersionName = resolvedInstanceName,
                    VersionType = string.Empty,
                    InstanceDirectory = instanceDirectory,
                    MemoryMb = settings.DefaultMemoryMb,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            };
        }
        finally
        {
            stagingLock.Release();
        }
    }

    public async Task<GameInstance> FinalizeAsync(
        StagedModpackInstance stagedInstance,
        string finalVersionName,
        CancellationToken cancellationToken = default)
    {
        var hadDefaultInstance = await instanceService.GetDefaultInstanceAsync(cancellationToken).ConfigureAwait(false) is not null;
        var instance = stagedInstance.Instance;
        instance.VersionName = stagedInstance.ResolvedInstanceName;
        instance.InstanceDirectory = stagedInstance.InstanceDirectory;
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        repository.CreateInstanceDirectories(instance.InstanceDirectory);

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

        if (!string.IsNullOrWhiteSpace(finalVersionName))
            TryDeleteVersionDirectory(settings.MinecraftDirectory, finalVersionName);

        if (!string.IsNullOrWhiteSpace(stagedInstance.ResolvedInstanceName))
            TryDeleteVersionDirectory(settings.MinecraftDirectory, stagedInstance.ResolvedInstanceName);
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

    private async Task<string> ResolveUniqueInstanceNameAsync(
        string preferredInstanceName,
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        var baseName = preferredInstanceName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseName))
            throw new ModpackImportException(ModpackImportFailureReason.InvalidManifest, "Prepared modpack package name is missing.");

        var unavailableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var instances = await instanceService.GetInstancesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var instance in instances)
        {
            AddUnavailableName(instance.Name);
            AddUnavailableName(instance.VersionName);
        }

        AddExistingVersionDirectoryNames(minecraftDirectory, unavailableNames);

        if (!unavailableNames.Contains(baseName))
            return baseName;

        var suffix = 1;
        while (true)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!unavailableNames.Contains(candidate))
                return candidate;

            suffix++;
        }

        void AddUnavailableName(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                unavailableNames.Add(name);
        }
    }

    private static void AddExistingVersionDirectoryNames(
        string minecraftDirectory,
        HashSet<string> unavailableNames)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return;

        foreach (var versionDirectory in Directory.EnumerateDirectories(versionsDirectory))
        {
            var versionName = Path.GetFileName(versionDirectory);
            if (!string.IsNullOrWhiteSpace(versionName))
                unavailableNames.Add(versionName);
        }
    }
}
