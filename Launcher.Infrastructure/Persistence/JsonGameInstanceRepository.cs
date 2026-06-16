using System.IO;
using System.Text.Json;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Persistence;

public sealed class JsonGameInstanceRepository : IGameInstanceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ISettingsService settingsService;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public JsonGameInstanceRepository(ISettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    public async Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var path = await GetInstancesPathAsync(cancellationToken);
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
                return [];

            await using var stream = File.OpenRead(path);
            var instances = await JsonSerializer.DeserializeAsync<List<GameInstance>>(stream, JsonOptions, cancellationToken);
            return instances ?? [];
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken = default)
    {
        var path = await GetInstancesPathAsync(cancellationToken);
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, instances, JsonOptions, cancellationToken);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public string GetUniqueInstanceDirectory(string dataDirectory, string name)
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

    public string GetVersionDirectory(string minecraftDirectory, string versionName)
    {
        return Path.Combine(minecraftDirectory, "versions", versionName);
    }

    public bool IsInstanceInstalled(GameInstance instance, string minecraftDirectory)
    {
        var versionName = GetVersionName(instance);
        if (string.IsNullOrWhiteSpace(versionName))
            return false;

        var versionJsonPath = Path.Combine(GetVersionDirectory(minecraftDirectory, versionName), $"{versionName}.json");
        if (!File.Exists(versionJsonPath))
            return false;

        try
        {
            using var stream = File.OpenRead(versionJsonPath);
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;

            if (root.TryGetProperty("inheritsFrom", out _))
                return true;

            var jarVersionName = versionName;
            if (root.TryGetProperty("jar", out var jarElement)
                && jarElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(jarElement.GetString()))
            {
                jarVersionName = jarElement.GetString()!;
            }

            var versionJarPath = Path.Combine(GetVersionDirectory(minecraftDirectory, jarVersionName), $"{jarVersionName}.jar");
            if (!File.Exists(versionJarPath))
                return false;

            return IsClientJarValid(root, versionJarPath);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void CreateInstanceDirectories(string directory)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "mods"));
        Directory.CreateDirectory(Path.Combine(directory, "config"));
        Directory.CreateDirectory(Path.Combine(directory, "saves"));
        Directory.CreateDirectory(Path.Combine(directory, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(directory, "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(directory, ".launcher", "disabled-mods"));
    }

    public void DeleteVersionDirectory(string minecraftDirectory, string versionName)
    {
        var versionDirectory = GetVersionDirectory(minecraftDirectory, versionName);
        if (Directory.Exists(versionDirectory))
            Directory.Delete(versionDirectory, recursive: true);
    }

    private async Task<string> GetInstancesPathAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return Path.Combine(settings.DataDirectory, "instances.json");
    }

    private static string GetVersionName(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
    }

    private static bool IsClientJarValid(JsonElement versionRoot, string versionJarPath)
    {
        if (!versionRoot.TryGetProperty("downloads", out var downloads)
            || !downloads.TryGetProperty("client", out var client))
        {
            return true;
        }

        if (client.TryGetProperty("size", out var sizeElement)
            && sizeElement.TryGetInt64(out var expectedSize)
            && expectedSize > 0
            && new FileInfo(versionJarPath).Length != expectedSize)
        {
            return false;
        }

        return true;
    }
}
