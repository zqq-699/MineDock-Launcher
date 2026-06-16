using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Repositories;
using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed class GameInstanceService : IGameInstanceService
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceRepository repository;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers;
    private readonly SemaphoreSlim installExecutionLock = new(1, 1);

    public GameInstanceService(
        ISettingsService settingsService,
        IGameInstanceRepository repository,
        IEnumerable<ILoaderProvider> providers)
    {
        this.settingsService = settingsService;
        this.repository = repository;
        this.providers = providers.ToDictionary(provider => provider.Kind);
    }

    public async Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<GameInstance>> GetInstancesCoreAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        var storedInstances = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var installedVersions = await repository
            .DiscoverInstalledVersionsAsync(settings.MinecraftDirectory, cancellationToken)
            .ConfigureAwait(false);
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
            await repository.SaveAllAsync(syncedInstances, cancellationToken).ConfigureAwait(false);

        if (defaultChanged)
            await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);

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
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
            throw new NotSupportedException($"{loader} is not implemented yet.");

        var lockAcquiredImmediately = installExecutionLock.Wait(0, cancellationToken);
        if (!lockAcquiredImmediately)
        {
            progress?.Report(new LauncherProgress("Queue", "等待其他安装任务完成"));
            await installExecutionLock.WaitAsync(cancellationToken);
        }

        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var versionIdentity = SanitizeName(
                string.IsNullOrWhiteSpace(name)
                    ? $"{minecraftVersion} {provider.DisplayName}"
                    : name);
            var cleanupCandidates = CreateCleanupCandidates(settings.MinecraftDirectory, minecraftVersion, versionIdentity, loader);
            var instances = (await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false)).ToList();
            if (instances.Any(instance => IsSameVersionIdentity(instance, versionIdentity)))
                throw new InvalidOperationException("已存在同名游戏。");

            try
            {
                var versionName = await provider.InstallAsync(
                    minecraftVersion,
                    settings.MinecraftDirectory,
                    versionIdentity,
                    loaderVersion,
                    progress,
                    cancellationToken).ConfigureAwait(false);

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
                    JavaPath = settings.DefaultJavaPath,
                    MemoryMb = settings.DefaultMemoryMb,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                instances.Add(instance);
                await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(settings.DefaultInstanceId))
                {
                    settings.DefaultInstanceId = instance.Id;
                    await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
                }

                return instance;
            }
            catch
            {
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

        if (string.Equals(settings.DefaultInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.DefaultInstanceId = instances.FirstOrDefault()?.Id ?? string.Empty;
            await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
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
            JavaPath = settings.DefaultJavaPath,
            MemoryMb = settings.DefaultMemoryMb,
            CreatedAt = installedVersion.DiscoveredAt,
            UpdatedAt = installedVersion.DiscoveredAt
        };
    }

    private static bool ApplyInstalledVersion(GameInstance instance, InstalledGameVersion installedVersion)
    {
        var changed = false;
        changed |= SetIfChanged(instance.Name, installedVersion.VersionName, value => instance.Name = value ?? string.Empty);
        changed |= SetIfChanged(instance.MinecraftVersion, installedVersion.MinecraftVersion, value => instance.MinecraftVersion = value ?? string.Empty);
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
