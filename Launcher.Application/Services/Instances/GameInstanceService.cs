using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Repositories;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed class GameInstanceService : IGameInstanceService
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceRepository repository;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers;
    private readonly IModrinthService? modrinthService;
    private readonly ILogger<GameInstanceService> logger;
    private readonly SemaphoreSlim installExecutionLock = new(1, 1);
    private readonly HashSet<string> installingVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object installingVersionsLock = new();

    public GameInstanceService(
        ISettingsService settingsService,
        IGameInstanceRepository repository,
        IEnumerable<ILoaderProvider> providers,
        IModrinthService? modrinthService = null,
        ILogger<GameInstanceService>? logger = null)
    {
        this.settingsService = settingsService;
        this.repository = repository;
        this.providers = providers.ToDictionary(provider => provider.Kind);
        this.modrinthService = modrinthService;
        this.logger = logger ?? NullLogger<GameInstanceService>.Instance;
    }

    public async Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        logger.LogDebug(
            "Game instances refreshed. Count={InstanceCount} DefaultInstanceId={DefaultInstanceId}",
            instances.Count,
            settings.DefaultInstanceId);
        return instances;
    }

    private async Task<IReadOnlyList<GameInstance>> GetInstancesCoreAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        var storedInstances = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var installedVersions = (await repository
            .DiscoverInstalledVersionsAsync(settings.MinecraftDirectory, cancellationToken)
            .ConfigureAwait(false))
            .Where(version => !IsInstallingVersion(settings.MinecraftDirectory, version.VersionName))
            .ToList();
        var syncedInstances = SynchronizeInstalledInstances(
            storedInstances,
            installedVersions,
            settings,
            out var instancesChanged);

        var defaultChanged = false;
        if (!string.IsNullOrWhiteSpace(settings.DefaultInstanceId)
            && syncedInstances.All(instance => instance.Id != settings.DefaultInstanceId))
        {
            settings.DefaultInstanceId = syncedInstances.FirstOrDefault()?.Id ?? string.Empty;
            defaultChanged = true;
        }

        if (instancesChanged)
        {
            logger.LogDebug(
                "Persisting synchronized game instances. StoredCount={StoredCount} InstalledCount={InstalledCount} SyncedCount={SyncedCount}",
                storedInstances.Count,
                installedVersions.Count,
                syncedInstances.Count);
            await repository.SaveAllAsync(syncedInstances, cancellationToken).ConfigureAwait(false);
        }

        if (defaultChanged)
        {
            logger.LogDebug("Default game instance reset during synchronization. DefaultInstanceId={DefaultInstanceId}", settings.DefaultInstanceId);
            await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        return syncedInstances;
    }

    public async Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        return instances.FirstOrDefault(instance => instance.Id == settings.DefaultInstanceId)
            ?? instances.FirstOrDefault();
    }

    public async Task<GameInstance> CreateInstanceAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string? name,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0,
        bool installFabricApi = true)
    {
        if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
            throw new NotSupportedException($"{loader} is not implemented yet.");

        logger.LogInformation(
            "Creating game instance. MinecraftVersion={MinecraftVersion} Loader={Loader} LoaderVersion={LoaderVersion} RequestedName={RequestedName}",
            minecraftVersion,
            loader,
            loaderVersion,
            name);

        var lockAcquiredImmediately = installExecutionLock.Wait(0, cancellationToken);
        if (!lockAcquiredImmediately)
        {
            logger.LogInformation("Game instance install queued because another install is running.");
            progress?.Report(new LauncherProgress(InstallProgressStages.Queue, string.Empty));
            await installExecutionLock.WaitAsync(cancellationToken);
        }

        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var versionIdentity = SanitizeName(
                string.IsNullOrWhiteSpace(name)
                    ? CreateDefaultInstanceName(minecraftVersion, loader, loaderVersion)
                    : name);
            var cleanupCandidates = CreateCleanupCandidates(settings.MinecraftDirectory, minecraftVersion, versionIdentity, loader);
            var instances = (await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false)).ToList();
            if (instances.Any(instance => IsSameVersionIdentity(instance, versionIdentity)))
                throw new DuplicateGameInstanceNameException(versionIdentity);

            try
            {
                TrackInstallingVersion(settings.MinecraftDirectory, versionIdentity);

                var versionName = await provider.InstallAsync(
                    minecraftVersion,
                    settings.MinecraftDirectory,
                    versionIdentity,
                    loaderVersion,
                    progress,
                    downloadSourcePreference,
                    cancellationToken,
                    downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
                logger.LogInformation(
                    "Game version installed. VersionIdentity={VersionIdentity} VersionName={VersionName} Loader={Loader}",
                    versionIdentity,
                    versionName,
                    loader);

                var instanceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, versionName);
                repository.CreateInstanceDirectories(instanceDirectory);

                var now = DateTimeOffset.UtcNow;
                var instance = new GameInstance
                {
                    Name = versionIdentity,
                    MinecraftVersion = minecraftVersion,
                    Loader = loader,
                    LoaderVersion = loader == LoaderKind.Vanilla ? null : loaderVersion,
                    VersionName = versionName,
                    VersionType = string.Empty,
                    InstanceDirectory = instanceDirectory,
                    MemoryMb = settings.DefaultMemoryMb,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                if (loader is LoaderKind.Fabric && installFabricApi && modrinthService is not null)
                    await modrinthService.InstallFabricApiAsync(instance, progress, cancellationToken).ConfigureAwait(false);

                instances.Add(instance);
                await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(settings.DefaultInstanceId))
                {
                    settings.DefaultInstanceId = instance.Id;
                    await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Default game instance initialized. InstanceId={InstanceId}", instance.Id);
                }

                UntrackInstallingVersion(settings.MinecraftDirectory, versionIdentity);
                logger.LogInformation(
                    "Game instance created. InstanceId={InstanceId} VersionName={VersionName} InstanceDirectory={InstanceDirectory}",
                    instance.Id,
                    instance.VersionName,
                    instance.InstanceDirectory);
                return instance;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Game instance creation failed. VersionIdentity={VersionIdentity} Loader={Loader}",
                    versionIdentity,
                    loader);
                UntrackInstallingVersion(settings.MinecraftDirectory, versionIdentity);
                TryCleanupCreatedVersionDirectories(settings.MinecraftDirectory, cleanupCandidates);
                throw;
            }
        }
        finally
        {
            installExecutionLock.Release();
        }
    }

    public async Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        var instances = (await GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var index = instances.FindIndex(existing => existing.Id == instance.Id);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        if (index >= 0)
            instances[index] = instance;
        else
            instances.Add(instance);

        await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Game instance saved. InstanceId={InstanceId} VersionName={VersionName}", instance.Id, instance.VersionName);
    }

    public async Task<GameInstance> RenameInstanceAsync(
        string instanceId,
        string? newName,
        string? newIconSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new InvalidOperationException("Instance id is required.");

        var instances = (await GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Instance was not found.");

        var sanitizedName = SanitizeName(newName ?? string.Empty);
        var normalizedIconSource = string.IsNullOrWhiteSpace(newIconSource) ? null : newIconSource.Trim();
        var currentVersionName = GetVersionName(instance);

        if (instances.Any(existing =>
                !string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(existing.Name, sanitizedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.VersionName, sanitizedName, StringComparison.OrdinalIgnoreCase))))
        {
            throw new DuplicateGameInstanceNameException(sanitizedName);
        }

        var nameChanged = !string.Equals(currentVersionName, sanitizedName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(instance.Name, sanitizedName, StringComparison.Ordinal);
        var iconChanged = !string.Equals(instance.IconSource, normalizedIconSource, StringComparison.Ordinal);

        if (!nameChanged && !iconChanged)
            return instance;

        if (!string.Equals(currentVersionName, sanitizedName, StringComparison.OrdinalIgnoreCase))
        {
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            await repository.RenameVersionAsync(
                settings.MinecraftDirectory,
                currentVersionName,
                sanitizedName,
                cancellationToken).ConfigureAwait(false);

            instance.VersionName = sanitizedName;
            instance.InstanceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, sanitizedName);
        }

        instance.Name = sanitizedName;
        instance.IconSource = normalizedIconSource;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var index = instances.FindIndex(existing =>
            string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            instances[index] = instance;

        await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Game instance renamed. InstanceId={InstanceId} VersionName={VersionName} IconChanged={IconChanged}",
            instance.Id,
            instance.VersionName,
            iconChanged);
        return instance;
    }

    public async Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
            return false;

        if (string.Equals(settings.DefaultInstanceId, instance.Id, StringComparison.Ordinal))
            return true;

        settings.DefaultInstanceId = instance.Id;
        await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Default game instance changed. InstanceId={InstanceId}", instance.Id);
        return true;
    }

    public async Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = (await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false)).ToList();
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
            return false;

        instances.Remove(instance);

        var versionName = GetVersionName(instance);
        if (!string.IsNullOrWhiteSpace(versionName))
            repository.DeleteVersionDirectory(settings.MinecraftDirectory, versionName);

        await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Game instance deleted. InstanceId={InstanceId} VersionName={VersionName}", instance.Id, versionName);

        if (string.Equals(settings.DefaultInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.DefaultInstanceId = instances.FirstOrDefault()?.Id ?? string.Empty;
            await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Default game instance changed after deletion. DefaultInstanceId={DefaultInstanceId}", settings.DefaultInstanceId);
        }

        return true;
    }

    private static bool IsSameVersionIdentity(GameInstance instance, string versionName)
    {
        return string.Equals(instance.Name, versionName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.VersionName, versionName, StringComparison.OrdinalIgnoreCase);
    }

    private List<GameInstance> SynchronizeInstalledInstances(
        List<GameInstance> instances,
        IReadOnlyList<InstalledGameVersion> installedVersions,
        LauncherSettings settings,
        out bool changed)
    {
        changed = false;
        var syncedInstances = new List<GameInstance>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedByVersion = installedVersions
            .Where(version => !string.IsNullOrWhiteSpace(version.VersionName))
            .GroupBy(version => version.VersionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            var versionName = GetVersionName(instance);
            if (string.IsNullOrWhiteSpace(versionName)
                || !installedByVersion.TryGetValue(versionName, out var installedVersion)
                || !seenVersions.Add(installedVersion.VersionName))
            {
                changed = true;
                continue;
            }

            changed |= ApplyInstalledVersion(instance, installedVersion);
            repository.CreateInstanceDirectories(installedVersion.Directory);
            syncedInstances.Add(instance);
        }

        foreach (var installedVersion in installedVersions)
        {
            if (string.IsNullOrWhiteSpace(installedVersion.VersionName)
                || !seenVersions.Add(installedVersion.VersionName))
            {
                continue;
            }

            repository.CreateInstanceDirectories(installedVersion.Directory);
            syncedInstances.Add(CreateDiscoveredInstance(installedVersion, settings));
            changed = true;
        }

        return syncedInstances;
    }

    private GameInstance CreateDiscoveredInstance(
        InstalledGameVersion installedVersion,
        LauncherSettings settings)
    {
        return new GameInstance
        {
            Id = CreateDiscoveredInstanceId(settings.MinecraftDirectory, installedVersion.VersionName),
            Name = installedVersion.VersionName,
            MinecraftVersion = installedVersion.MinecraftVersion,
            Loader = installedVersion.Loader,
            LoaderVersion = installedVersion.LoaderVersion,
            VersionName = installedVersion.VersionName,
            VersionType = installedVersion.VersionType,
            InstanceDirectory = installedVersion.Directory,
            MemoryMb = settings.DefaultMemoryMb,
            CreatedAt = installedVersion.DiscoveredAt,
            UpdatedAt = installedVersion.DiscoveredAt
        };
    }

    private static bool ApplyInstalledVersion(GameInstance instance, InstalledGameVersion installedVersion)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(instance.Name))
            changed |= SetIfChanged(instance.Name, installedVersion.VersionName, value => instance.Name = value ?? string.Empty);
        changed |= SetIfChanged(
            instance.MinecraftVersion,
            ResolvePreferredMinecraftVersion(instance.MinecraftVersion, installedVersion.MinecraftVersion, installedVersion.VersionName),
            value => instance.MinecraftVersion = value ?? string.Empty);
        changed |= SetIfChanged(instance.VersionName, installedVersion.VersionName, value => instance.VersionName = value ?? string.Empty);
        changed |= SetIfChanged(instance.VersionType, installedVersion.VersionType, value => instance.VersionType = value ?? string.Empty);
        changed |= SetIfChanged(instance.LoaderVersion, installedVersion.LoaderVersion, value => instance.LoaderVersion = value);
        changed |= SetIfChanged(instance.InstanceDirectory, installedVersion.Directory, value => instance.InstanceDirectory = value ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (instance.Loader != installedVersion.Loader)
        {
            instance.Loader = installedVersion.Loader;
            changed = true;
        }

        return changed;
    }

    private static bool SetIfChanged(
        string? currentValue,
        string? nextValue,
        Action<string?> setValue,
        StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.Equals(currentValue, nextValue, comparison))
            return false;

        setValue(nextValue);
        return true;
    }

    private static string ResolvePreferredMinecraftVersion(
        string? currentMinecraftVersion,
        string? discoveredMinecraftVersion,
        string versionName)
    {
        if (LooksLikeMinecraftVersion(discoveredMinecraftVersion))
            return discoveredMinecraftVersion ?? string.Empty;

        if (LooksLikeMinecraftVersion(currentMinecraftVersion)
            && !string.Equals(currentMinecraftVersion, versionName, StringComparison.OrdinalIgnoreCase))
        {
            return currentMinecraftVersion ?? string.Empty;
        }

        return discoveredMinecraftVersion ?? string.Empty;
    }

    private static bool LooksLikeMinecraftVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Version.TryParse(value, out _))
            return true;

        if (value.Length >= 6
            && char.IsDigit(value[0])
            && char.IsDigit(value[1])
            && value[2] == 'w'
            && char.IsDigit(value[3])
            && char.IsDigit(value[4]))
        {
            return true;
        }

        return value.StartsWith("a", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("b", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateDiscoveredInstanceId(string minecraftDirectory, string versionName)
    {
        var versionPath = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions", versionName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(versionPath));
        return $"local-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static string GetVersionName(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Minecraft" : sanitized;
    }

    private static string CreateDefaultInstanceName(string minecraftVersion, LoaderKind loader, string? loaderVersion)
    {
        return loader switch
        {
            LoaderKind.Fabric when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-fabric-{loaderVersion}",
            LoaderKind.Fabric => $"{minecraftVersion}-fabric",
            LoaderKind.Forge when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-forge-{loaderVersion}",
            LoaderKind.Forge => $"{minecraftVersion}-forge",
            LoaderKind.NeoForge when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-neoforge-{loaderVersion}",
            LoaderKind.NeoForge => $"{minecraftVersion}-neoforge",
            LoaderKind.Quilt when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-quilt-{loaderVersion}",
            LoaderKind.Quilt => $"{minecraftVersion}-quilt",
            _ => minecraftVersion
        };
    }

    private void TrackInstallingVersion(string minecraftDirectory, string versionName)
    {
        lock (installingVersionsLock)
            installingVersions.Add(CreateInstallingVersionKey(minecraftDirectory, versionName));
    }

    private void UntrackInstallingVersion(string minecraftDirectory, string versionName)
    {
        lock (installingVersionsLock)
            installingVersions.Remove(CreateInstallingVersionKey(minecraftDirectory, versionName));
    }

    private bool IsInstallingVersion(string minecraftDirectory, string versionName)
    {
        lock (installingVersionsLock)
            return installingVersions.Contains(CreateInstallingVersionKey(minecraftDirectory, versionName));
    }

    private static string CreateInstallingVersionKey(string minecraftDirectory, string versionName)
    {
        return Path.GetFullPath(Path.Combine(minecraftDirectory, "versions", versionName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }

    private IReadOnlyList<VersionCleanupCandidate> CreateCleanupCandidates(
        string minecraftDirectory,
        string minecraftVersion,
        string versionIdentity,
        LoaderKind loader)
    {
        var candidates = new List<VersionCleanupCandidate>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            versionIdentity
        };

        if (loader is LoaderKind.Vanilla)
            names.Add(minecraftVersion);

        foreach (var versionName in names)
        {
            var directory = repository.GetVersionDirectory(minecraftDirectory, versionName);
            candidates.Add(new VersionCleanupCandidate(versionName, Directory.Exists(directory)));
        }

        return candidates;
    }

    private void TryCleanupCreatedVersionDirectories(
        string minecraftDirectory,
        IReadOnlyList<VersionCleanupCandidate> cleanupCandidates)
    {
        foreach (var candidate in cleanupCandidates)
        {
            if (candidate.ExistedBeforeInstall)
                continue;

            try
            {
                repository.DeleteVersionDirectory(minecraftDirectory, candidate.VersionName);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record VersionCleanupCandidate(string VersionName, bool ExistedBeforeInstall);
}
