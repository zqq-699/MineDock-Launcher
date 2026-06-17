using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Infrastructure.Persistence;

public sealed class JsonGameInstanceRepository : IGameInstanceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string LauncherDirectoryName = ".launcher";
    private const string InstanceSettingsFileName = "instance-settings.json";
    private readonly ISettingsService settingsService;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public JsonGameInstanceRepository(ISettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    public async Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadPerInstanceSettingsAsync(settings.MinecraftDirectory, cancellationToken);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            var persistedSettingsPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var persistedVersionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var instance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var versionName = GetVersionName(instance);
                if (string.IsNullOrWhiteSpace(versionName))
                    continue;

                if (!persistedVersionNames.Add(versionName))
                    continue;

                var versionDirectory = ResolveVersionDirectory(settings.MinecraftDirectory, instance, versionName);
                if (!Directory.Exists(versionDirectory))
                    continue;

                var settingsPath = GetInstanceSettingsPath(versionDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

                await using var stream = File.Create(settingsPath);
                var snapshot = CreateStorageSnapshot(instance, versionDirectory, versionName);
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
                persistedSettingsPaths.Add(Path.GetFullPath(settingsPath));
            }

            CleanupOrphanedInstanceSettingsFiles(settings.MinecraftDirectory, persistedSettingsPaths);
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

    public async Task RenameVersionAsync(
        string minecraftDirectory,
        string oldVersionName,
        string newVersionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldVersionName)
            || string.IsNullOrWhiteSpace(newVersionName)
            || string.Equals(oldVersionName, newVersionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var sourceDirectory = Path.Combine(versionsDirectory, oldVersionName);
        var destinationDirectory = Path.Combine(versionsDirectory, newVersionName);
        var stagingDirectory = Path.Combine(versionsDirectory, $".launcher-rename-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(versionsDirectory, $".launcher-backup-{Guid.NewGuid():N}");
        var sourceJsonPath = Path.Combine(sourceDirectory, $"{oldVersionName}.json");

        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Version directory not found: {sourceDirectory}");

        if (!File.Exists(sourceJsonPath))
            throw new FileNotFoundException($"Version JSON not found: {sourceJsonPath}", sourceJsonPath);

        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {destinationDirectory}");

        var versionJson = await ReadVersionJsonAsync(sourceJsonPath, cancellationToken);
        RewriteVersionIdentity(versionJson, oldVersionName, newVersionName);

        try
        {
            await CopyVersionDirectoryAsync(
                sourceDirectory,
                stagingDirectory,
                oldVersionName,
                newVersionName,
                versionJson,
                cancellationToken);

            Directory.Move(sourceDirectory, backupDirectory);

            try
            {
                Directory.Move(stagingDirectory, destinationDirectory);
                Directory.Delete(backupDirectory, recursive: true);
            }
            catch
            {
                if (Directory.Exists(destinationDirectory))
                    Directory.Delete(destinationDirectory, recursive: true);

                if (Directory.Exists(backupDirectory) && !Directory.Exists(sourceDirectory))
                    Directory.Move(backupDirectory, sourceDirectory);

                throw;
            }
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static string GetInstanceSettingsPath(string versionDirectory)
    {
        return Path.Combine(versionDirectory, LauncherDirectoryName, InstanceSettingsFileName);
    }

    private static string ResolveVersionDirectory(string minecraftDirectory, GameInstance instance, string versionName)
    {
        return Path.Combine(minecraftDirectory, "versions", versionName);
    }

    private static async Task<List<GameInstance>> ReadPerInstanceSettingsAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return [];

        var storedInstances = new List<GameInstance>();
        foreach (var versionDirectory in EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var settingsPath = GetInstanceSettingsPath(versionDirectory);
            if (!File.Exists(settingsPath))
                continue;

            var instance = await TryReadInstanceSettingsAsync(settingsPath, cancellationToken);
            if (instance is null)
                continue;

            var versionName = Path.GetFileName(versionDirectory);
            if (string.IsNullOrWhiteSpace(instance.VersionName))
                instance.VersionName = versionName;

            instance.InstanceDirectory = versionDirectory;
            storedInstances.Add(instance);
        }

        return storedInstances;
    }

    private static async Task<GameInstance?> TryReadInstanceSettingsAsync(
        string settingsPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            return await JsonSerializer.DeserializeAsync<GameInstance>(stream, JsonOptions, cancellationToken);
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

    private static GameInstance CreateStorageSnapshot(GameInstance instance, string versionDirectory, string versionName)
    {
        return new GameInstance
        {
            Id = instance.Id,
            Name = instance.Name,
            MinecraftVersion = instance.MinecraftVersion,
            Loader = instance.Loader,
            LoaderVersion = instance.LoaderVersion,
            VersionName = versionName,
            VersionType = instance.VersionType,
            Description = instance.Description,
            IconSource = instance.IconSource,
            InstanceDirectory = versionDirectory,
            JavaPath = instance.JavaPath,
            MemoryMb = instance.MemoryMb,
            WindowWidth = instance.WindowWidth,
            WindowHeight = instance.WindowHeight,
            JvmArguments = instance.JvmArguments,
            LaunchSettingsMode = instance.LaunchSettingsMode,
            CheckFilesBeforeLaunch = instance.CheckFilesBeforeLaunch,
            AutoRepairMissingFiles = instance.AutoRepairMissingFiles,
            MinimizeLauncherAfterLaunch = instance.MinimizeLauncherAfterLaunch,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt
        };
    }

    private static void CleanupOrphanedInstanceSettingsFiles(
        string minecraftDirectory,
        IReadOnlySet<string> persistedSettingsPaths)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return;

        foreach (var versionDirectory in EnumerateDirectories(versionsDirectory))
        {
            var settingsPath = GetInstanceSettingsPath(versionDirectory);
            if (!File.Exists(settingsPath))
                continue;

            var fullSettingsPath = Path.GetFullPath(settingsPath);
            if (persistedSettingsPaths.Contains(fullSettingsPath))
                continue;

            try
            {
                File.Delete(fullSettingsPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<JsonObject> ReadVersionJsonAsync(string versionJsonPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(versionJsonPath);
        var jsonNode = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Version JSON is empty: {versionJsonPath}");
        return jsonNode.AsObject();
    }

    private static void RewriteVersionIdentity(JsonObject versionObject, string oldVersionName, string newVersionName)
    {
        versionObject["id"] = newVersionName;

        if (versionObject["jar"] is JsonValue jarValue
            && string.Equals(jarValue.ToString(), oldVersionName, StringComparison.OrdinalIgnoreCase))
        {
            versionObject["jar"] = newVersionName;
        }
    }

    private static async Task CopyVersionDirectoryAsync(
        string sourceDirectory,
        string stagingDirectory,
        string oldVersionName,
        string newVersionName,
        JsonObject rewrittenVersionJson,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
                var destinationRelativePath = RewriteTopLevelVersionFileName(relativePath, oldVersionName, newVersionName);
                var destinationPath = Path.Combine(stagingDirectory, destinationRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                if (string.Equals(relativePath, $"{oldVersionName}.json", StringComparison.OrdinalIgnoreCase))
                {
                    await File.WriteAllTextAsync(
                        destinationPath,
                        rewrittenVersionJson.ToJsonString(JsonOptions),
                        cancellationToken);
                    continue;
                }

                File.Copy(sourceFilePath, destinationPath, overwrite: false);
            }
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);

            throw;
        }
    }

    private static string RewriteTopLevelVersionFileName(string relativePath, string oldVersionName, string newVersionName)
    {
        if (string.Equals(relativePath, $"{oldVersionName}.json", StringComparison.OrdinalIgnoreCase))
            return $"{newVersionName}.json";

        if (string.Equals(relativePath, $"{oldVersionName}.jar", StringComparison.OrdinalIgnoreCase))
            return $"{newVersionName}.jar";

        return relativePath;
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
            var loader = ResolveLoaderFromLibraryName(libraryName);
            if (loader.Kind is not LoaderKind.Vanilla)
                return loader;
        }

        return ResolveLoaderFromText(
            $"{metadata.Id} {metadata.VersionName}",
            allowLooseMatch: true);
    }

    private static LoaderInfo ResolveLoaderFromLibraryName(string value)
    {
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return ResolveLoaderFromText(value, allowLooseMatch: false);

        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];

        if (group.Equals("net.neoforged", StringComparison.OrdinalIgnoreCase)
            && artifact.Equals("neoforge", StringComparison.OrdinalIgnoreCase))
        {
            return new LoaderInfo(LoaderKind.NeoForge, version);
        }

        if (group.Equals("org.quiltmc", StringComparison.OrdinalIgnoreCase)
            && artifact.Equals("quilt-loader", StringComparison.OrdinalIgnoreCase))
        {
            return new LoaderInfo(LoaderKind.Quilt, version);
        }

        if (group.Equals("net.fabricmc", StringComparison.OrdinalIgnoreCase)
            && artifact.Equals("fabric-loader", StringComparison.OrdinalIgnoreCase))
        {
            return new LoaderInfo(LoaderKind.Fabric, version);
        }

        if (group.Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase)
            && artifact.Equals("forge", StringComparison.OrdinalIgnoreCase))
        {
            return new LoaderInfo(LoaderKind.Forge, TryReadForgeVersion(version));
        }

        return new LoaderInfo(LoaderKind.Vanilla, null);
    }

    private static LoaderInfo ResolveLoaderFromText(string value, bool allowLooseMatch)
    {
        var normalized = value.ToLowerInvariant();
        if (normalized.Contains("net.neoforged:neoforge", StringComparison.Ordinal)
            || normalized.Contains(" neoforge-", StringComparison.Ordinal)
            || normalized.StartsWith("neoforge-", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("neoforge", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.NeoForge, TryReadMavenVersion(value));
        }

        if (normalized.Contains("org.quiltmc:quilt-loader", StringComparison.Ordinal)
            || normalized.Contains("quilt-loader", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("quilt", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.Quilt, TryReadMavenVersion(value));
        }

        if (normalized.Contains("net.fabricmc:fabric-loader", StringComparison.Ordinal)
            || normalized.Contains("fabric-loader", StringComparison.Ordinal)
            || (allowLooseMatch && normalized.Contains("fabric", StringComparison.Ordinal)))
        {
            return new LoaderInfo(LoaderKind.Fabric, TryReadMavenVersion(value));
        }

        if (normalized.Contains("net.minecraftforge:forge", StringComparison.Ordinal)
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
