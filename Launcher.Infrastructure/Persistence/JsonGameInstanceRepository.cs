using System.IO;
using System.Text.Json;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

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

    public Task<IReadOnlyList<InstalledGameVersion>> DiscoverInstalledVersionsAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return Task.FromResult<IReadOnlyList<InstalledGameVersion>>([]);

        var installedVersions = new List<InstalledGameVersion>();
        foreach (var versionDirectory in EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var versionName = Path.GetFileName(versionDirectory);
            if (string.IsNullOrWhiteSpace(versionName))
                continue;

            var metadata = TryReadVersionMetadata(versionDirectory, versionName);
            if (metadata is null)
                continue;

            var loader = ResolveLoader(metadata);
            installedVersions.Add(new InstalledGameVersion(
                metadata.VersionName,
                ResolveMinecraftVersion(metadata),
                ResolveVersionType(metadata, minecraftDirectory),
                loader.Kind,
                loader.Version,
                versionDirectory,
                ResolveDiscoveredAt(versionDirectory)));
        }

        return Task.FromResult<IReadOnlyList<InstalledGameVersion>>(installedVersions);
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

        return TryReadVersionMetadata(GetVersionDirectory(minecraftDirectory, versionName), versionName) is not null;
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

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static VersionJsonMetadata? TryReadVersionMetadata(string versionDirectory, string versionName)
    {
        var versionJsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        if (!File.Exists(versionJsonPath))
            return null;

        try
        {
            using var stream = File.OpenRead(versionJsonPath);
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            var libraryNames = ReadLibraryNames(root);
            return new VersionJsonMetadata(
                versionName,
                GetStringProperty(root, "id"),
                GetStringProperty(root, "inheritsFrom"),
                GetStringProperty(root, "jar"),
                GetStringProperty(root, "type"),
                LauncherVersionMetadata.ReadMinecraftVersion(root),
                ReadAssetIndexMinecraftVersion(root),
                libraryNames,
                TryResolveMinecraftVersionFromLibraries(libraryNames));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveMinecraftVersion(VersionJsonMetadata metadata)
    {
        return FirstNonEmpty(
            metadata.LauncherMinecraftVersion,
            metadata.InheritsFrom,
            metadata.AssetIndexId,
            metadata.LibraryMinecraftVersion,
            GetVersionLikeValueOrEmpty(metadata.Jar),
            GetVersionLikeValueOrEmpty(metadata.Id),
            GetVersionLikeValueOrEmpty(metadata.VersionName));
    }

    private static string ResolveVersionType(VersionJsonMetadata metadata, string minecraftDirectory)
    {
        var versionType = NormalizeVersionType(metadata.Type);
        if (!string.IsNullOrWhiteSpace(versionType))
            return versionType;

        if (!string.IsNullOrWhiteSpace(metadata.InheritsFrom))
        {
            versionType = TryReadInheritedVersionType(
                minecraftDirectory,
                metadata.InheritsFrom,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(versionType))
                return versionType;
        }

        return IsSnapshotVersionName(metadata.VersionName)
               || IsSnapshotVersionName(metadata.Id)
               || IsSnapshotVersionName(metadata.MinecraftVersion)
            ? "snapshot"
            : "release";
    }

    private static string TryReadInheritedVersionType(
        string minecraftDirectory,
        string versionName,
        HashSet<string> visitedVersions)
    {
        if (!visitedVersions.Add(versionName))
            return string.Empty;

        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        var metadata = TryReadVersionMetadata(versionDirectory, versionName);
        if (metadata is null)
            return string.Empty;

        var versionType = NormalizeVersionType(metadata.Type);
        if (!string.IsNullOrWhiteSpace(versionType))
            return versionType;

        return string.IsNullOrWhiteSpace(metadata.InheritsFrom)
            ? string.Empty
            : TryReadInheritedVersionType(minecraftDirectory, metadata.InheritsFrom, visitedVersions);
    }

    private static LoaderInfo ResolveLoader(VersionJsonMetadata metadata)
    {
        foreach (var libraryName in metadata.LibraryNames)
        {
            var loader = ResolveLoaderFromText(libraryName, allowLooseMatch: false);
            if (loader.Kind is not LoaderKind.Vanilla)
                return loader;
        }

        return ResolveLoaderFromText(
            $"{metadata.Id} {metadata.VersionName}",
            allowLooseMatch: true);
    }

    private static LoaderInfo ResolveLoaderFromText(string value, bool allowLooseMatch)
    {
        var normalized = value.ToLowerInvariant();
        if (normalized.Contains("net.neoforged", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("neoforge", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.NeoForge, TryReadMavenVersion(value));
        }

        if (normalized.Contains("org.quiltmc", StringComparison.Ordinal)
            || normalized.Contains("quilt-loader", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("quilt", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.Quilt, TryReadMavenVersion(value));
        }

        if (normalized.Contains("net.fabricmc", StringComparison.Ordinal)
            || normalized.Contains("fabric-loader", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("fabric", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.Fabric, TryReadMavenVersion(value));
        }

        if (normalized.Contains("net.minecraftforge", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("forge", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.Forge, TryReadMavenVersion(value));
        }

        return new LoaderInfo(LoaderKind.Vanilla, null);
    }

    private static string? TryReadMavenVersion(string value)
    {
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return null;

        if (parts[0].Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("forge", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadForgeVersion(parts[2]);
        }

        return parts[2];
    }

    private static string TryReadForgeVersion(string combinedVersion)
    {
        var separatorIndex = combinedVersion.IndexOf('-');
        return separatorIndex >= 0 && separatorIndex < combinedVersion.Length - 1
            ? combinedVersion[(separatorIndex + 1)..]
            : combinedVersion;
    }

    private static IReadOnlyList<string> ReadLibraryNames(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries)
            || libraries.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var library in libraries.EnumerateArray())
        {
            var name = GetStringProperty(library, "name");
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }

    private static string ReadAssetIndexMinecraftVersion(JsonElement root)
    {
        if (!root.TryGetProperty("assetIndex", out var assetIndex)
            || assetIndex.ValueKind is not JsonValueKind.Object)
        {
            return string.Empty;
        }

        var assetIndexId = GetStringProperty(assetIndex, "id");
        return LooksLikeMinecraftVersion(assetIndexId) ? assetIndexId : string.Empty;
    }

    private static string? TryResolveMinecraftVersionFromLibraries(IReadOnlyList<string> libraryNames)
    {
        foreach (var libraryName in libraryNames)
        {
            var minecraftVersion = TryResolveMinecraftVersionFromLibrary(libraryName);
            if (!string.IsNullOrWhiteSpace(minecraftVersion))
                return minecraftVersion;
        }

        return null;
    }

    private static string? TryResolveMinecraftVersionFromLibrary(string libraryName)
    {
        var parts = libraryName.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return null;

        if (parts[0].Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("forge", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = parts[2].IndexOf('-');
            return separatorIndex > 0 ? parts[2][..separatorIndex] : parts[2];
        }

        return null;
    }

    private static string GetStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property)
               && property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
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

    private static string GetVersionLikeValueOrEmpty(string? value)
    {
        return LooksLikeMinecraftVersion(value) ? value ?? string.Empty : string.Empty;
    }

    private static string NormalizeVersionType(string? type)
    {
        return type?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) switch
        {
            "release" => "release",
            "snapshot" => "snapshot",
            "old_beta" or "oldbeta" or "beta" => "old_beta",
            "old_alpha" or "oldalpha" or "alpha" => "old_alpha",
            _ => string.Empty
        };
    }

    private static bool IsSnapshotVersionName(string? version)
    {
        return !string.IsNullOrWhiteSpace(version)
            && version.Length >= 5
            && char.IsDigit(version[0])
            && char.IsDigit(version[1])
            && version[2] == 'w'
            && char.IsDigit(version[3])
            && char.IsDigit(version[4]);
    }

    private static DateTimeOffset ResolveDiscoveredAt(string versionDirectory)
    {
        try
        {
            var info = new DirectoryInfo(versionDirectory);
            return new DateTimeOffset(info.CreationTimeUtc);
        }
        catch (IOException)
        {
            return DateTimeOffset.UtcNow;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private sealed record VersionJsonMetadata(
        string VersionName,
        string Id,
        string InheritsFrom,
        string Jar,
        string Type,
        string LauncherMinecraftVersion,
        string AssetIndexId,
        IReadOnlyList<string> LibraryNames,
        string? LibraryMinecraftVersion)
    {
        public string MinecraftVersion => FirstNonEmpty(
            LauncherMinecraftVersion,
            InheritsFrom,
            AssetIndexId,
            LibraryMinecraftVersion,
            GetVersionLikeValueOrEmpty(Jar),
            GetVersionLikeValueOrEmpty(Id),
            GetVersionLikeValueOrEmpty(VersionName));
    }

    private sealed record LoaderInfo(LoaderKind Kind, string? Version);
}
