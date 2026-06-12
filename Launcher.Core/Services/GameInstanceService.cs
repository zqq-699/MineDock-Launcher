using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class GameInstanceService : IGameInstanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ISettingsService settingsService;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers;

    public GameInstanceService(ISettingsService settingsService, IEnumerable<ILoaderProvider> providers)
    {
        this.settingsService = settingsService;
        this.providers = providers.ToDictionary(provider => provider.Kind);
    }

    public async Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var path = await GetInstancesPathAsync(cancellationToken);
        if (!File.Exists(path))
            return [];

        await using var stream = File.OpenRead(path);
        var instances = await JsonSerializer.DeserializeAsync<List<GameInstance>>(stream, JsonOptions, cancellationToken);
        return instances ?? [];
    }

    public async Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var instances = await GetInstancesAsync(cancellationToken);
        return instances.FirstOrDefault(i => i.Id == settings.DefaultInstanceId) ?? instances.FirstOrDefault();
    }

    public async Task<GameInstance> CreateInstanceAsync(string minecraftVersion, LoaderKind loader, string? loaderVersion, string? name, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
            throw new NotSupportedException($"{loader} 暂未实现下载。");

        var settings = await settingsService.LoadAsync(cancellationToken);
        var safeName = SanitizeName(string.IsNullOrWhiteSpace(name) ? $"{minecraftVersion} {provider.DisplayName}" : name);
        var instanceDirectory = GetUniqueInstanceDirectory(settings.DataDirectory, safeName);
        CreateInstanceDirectories(instanceDirectory);

        var versionName = await provider.InstallAsync(minecraftVersion, instanceDirectory, loaderVersion, progress, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var instance = new GameInstance
        {
            Name = safeName,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader == LoaderKind.Vanilla ? null : loaderVersion,
            VersionName = versionName,
            InstanceDirectory = instanceDirectory,
            JavaPath = settings.DefaultJavaPath,
            MemoryMb = settings.DefaultMemoryMb,
            CreatedAt = now,
            UpdatedAt = now
        };

        var instances = (await GetInstancesAsync(cancellationToken)).ToList();
        instances.Add(instance);
        await SaveAllAsync(instances, cancellationToken);

        if (string.IsNullOrWhiteSpace(settings.DefaultInstanceId))
        {
            settings.DefaultInstanceId = instance.Id;
            await settingsService.SaveAsync(settings, cancellationToken);
        }

        return instance;
    }

    public async Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        var instances = (await GetInstancesAsync(cancellationToken)).ToList();
        var index = instances.FindIndex(existing => existing.Id == instance.Id);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        if (index >= 0)
            instances[index] = instance;
        else
            instances.Add(instance);

        await SaveAllAsync(instances, cancellationToken);
    }

    private async Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken)
    {
        var path = await GetInstancesPathAsync(cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, instances, JsonOptions, cancellationToken);
    }

    private async Task<string> GetInstancesPathAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return Path.Combine(settings.DataDirectory, "instances.json");
    }

    private static string GetUniqueInstanceDirectory(string dataDirectory, string name)
    {
        var baseDirectory = Path.Combine(dataDirectory, "instances");
        Directory.CreateDirectory(baseDirectory);

        var candidate = Path.Combine(baseDirectory, name);
        var suffix = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(baseDirectory, $"{name}-{suffix}");
            suffix++;
        }

        return candidate;
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Minecraft" : sanitized;
    }

    private static void CreateInstanceDirectories(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "mods"));
        Directory.CreateDirectory(Path.Combine(directory, "config"));
        Directory.CreateDirectory(Path.Combine(directory, "saves"));
        Directory.CreateDirectory(Path.Combine(directory, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(directory, "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(directory, ".launcher", "disabled-mods"));
    }
}
