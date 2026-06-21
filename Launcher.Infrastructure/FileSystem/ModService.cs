using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class ModService : IModService
{
    private const string EnabledModExtension = ".jar";
    private const string DisabledModExtension = ".jar.disabled";
    private static readonly Regex TomlLogoFileRegex = new(
        "^[\\t ]*logoFile[\\t ]*=[\\t ]*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)')",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex TomlDisplayNameRegex = new(
        "^[\\t ]*displayName[\\t ]*=[\\t ]*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)')",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex TomlModIdRegex = new(
        "^[\\t ]*modId[\\t ]*=[\\t ]*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)')",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex TomlVersionRegex = new(
        "^[\\t ]*version[\\t ]*=[\\t ]*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)')",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<ModService> logger;
    private readonly string iconCacheDirectory;

    public ModService(LauncherPathProvider? pathProvider = null, ILogger<ModService>? logger = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.logger = logger ?? NullLogger<ModService>.Instance;
        iconCacheDirectory = Path.Combine(this.pathProvider.DefaultDataDirectory, "cache", "mods", "icons");
    }

    public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<LocalMod>>(
            () =>
            {
                var mods = new List<LocalMod>();
                var modsDirectory = GetModsDirectory(instance);
                Directory.CreateDirectory(modsDirectory);

                foreach (var file in Directory.EnumerateFiles(modsDirectory, $"*{EnabledModExtension}"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    mods.Add(ToLocalMod(file));
                }

                foreach (var file in Directory.EnumerateFiles(modsDirectory, $"*{DisabledModExtension}"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    mods.Add(ToLocalMod(file));
                }

                var result = mods
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                logger.LogInformation(
                    "Local mods loaded. InstanceId={InstanceId} Count={ModCount}",
                    instance.Id,
                    result.Count);
                return result;
            },
            cancellationToken);
    }

    public async Task<LocalMod> ImportAsync(
        GameInstance instance,
        string sourceJarPath,
        bool overwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceJarPath))
            throw new ModFileImportNotFoundException(sourceJarPath);

        var modsDirectory = GetModsDirectory(instance);
        Directory.CreateDirectory(modsDirectory);

        var destination = Path.Combine(modsDirectory, Path.GetFileName(sourceJarPath));
        if (File.Exists(destination) && !overwriteExisting)
        {
            var name = Path.GetFileNameWithoutExtension(sourceJarPath);
            destination = Path.Combine(modsDirectory, $"{name}-{DateTimeOffset.Now:yyyyMMddHHmmss}.jar");
        }

        await using var source = File.OpenRead(sourceJarPath);
        await using var target = new FileStream(
            destination,
            overwriteExisting ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await source.CopyToAsync(target, cancellationToken);
        logger.LogInformation(
            "Local mod imported. InstanceId={InstanceId} FileName={FileName} Destination={Destination} OverwriteExisting={OverwriteExisting}",
            instance.Id,
            Path.GetFileName(destination),
            destination,
            overwriteExisting);
        return ToLocalMod(destination);
    }

    public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default)
    {
        if (mod.IsEnabled == enabled)
            return Task.CompletedTask;

        var current = mod.FullPath;
        var targetPath = enabled
            ? GetEnabledModPath(current)
            : GetDisabledModPath(current);

        File.Move(current, targetPath, overwrite: true);
        logger.LogInformation(
            "Local mod enabled state changed. FileName={FileName} Enabled={Enabled} TargetPath={TargetPath}",
            mod.FileName,
            enabled,
            targetPath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
    {
        if (File.Exists(mod.FullPath))
        {
            File.Delete(mod.FullPath);
            logger.LogInformation("Local mod deleted. FileName={FileName}", mod.FileName);
        }

        return Task.CompletedTask;
    }

    private LocalMod ToLocalMod(string path)
    {
        var info = new FileInfo(path);
        var metadata = TryResolveMetadata(info);
        var enabled = IsEnabledModPath(path);
        return new LocalMod
        {
            Name = string.IsNullOrWhiteSpace(metadata.DisplayName)
                ? GetDisplayFileNameWithoutModExtensions(path)
                : metadata.DisplayName,
            Loader = metadata.Loader,
            ModId = metadata.ModId,
            Version = metadata.Version,
            FileName = Path.GetFileName(path),
            FullPath = path,
            IconSource = metadata.IconSource,
            IsEnabled = enabled,
            SizeBytes = info.Length,
            Source = "Local"
        };
    }

    private ResolvedModMetadata TryResolveMetadata(FileInfo jarFile)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarFile.FullName);
            var declaration = TryFindMetadataDeclaration(archive);
            var iconSource = TryResolveIconSource(jarFile, archive, declaration);
            return new ResolvedModMetadata(
                declaration?.DisplayName,
                declaration?.Loader,
                declaration?.ModId,
                declaration?.Version,
                iconSource);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to parse mod metadata while resolving display information. FileName={FileName}",
                jarFile.Name);
            return ResolvedModMetadata.Empty;
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to read mod jar while resolving display information. FileName={FileName}",
                jarFile.Name);
            return ResolvedModMetadata.Empty;
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to decode embedded mod icon. FileName={FileName}",
                jarFile.Name);
            return ResolvedModMetadata.Empty;
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to cache embedded mod icon. FileName={FileName}",
                jarFile.Name);
            return ResolvedModMetadata.Empty;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unexpected error while resolving mod display information. FileName={FileName}",
                jarFile.Name);
            return ResolvedModMetadata.Empty;
        }
    }

    private string? TryResolveIconSource(
        FileInfo jarFile,
        ZipArchive archive,
        MetadataDeclaration? declaration)
    {
        if (declaration?.IconPath is null)
        {
            logger.LogDebug(
                "No embedded mod icon declared. FileName={FileName}",
                jarFile.Name);
            return null;
        }

        var iconEntry = TryResolveIconEntry(archive, declaration.MetadataEntryName, declaration.IconPath);
        if (iconEntry is null)
        {
            logger.LogDebug(
                "Embedded mod icon declaration could not be resolved. FileName={FileName} IconPath={IconPath}",
                jarFile.Name,
                declaration.IconPath);
            return null;
        }

        return CacheIconAndGetSource(jarFile, iconEntry);
    }

    private string CacheIconAndGetSource(FileInfo jarFile, ZipArchiveEntry iconEntry)
    {
        Directory.CreateDirectory(iconCacheDirectory);
        var cachePath = GetCachePath(jarFile, iconEntry.FullName);
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

        logger.LogDebug(
            "Embedded mod icon cached. FileName={FileName} CachePath={CachePath}",
            jarFile.Name,
            cachePath);
        return new Uri(cachePath).AbsoluteUri;
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
            ?? throw new InvalidDataException("Embedded mod icon contains no frames.");
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

    private string GetCachePath(FileInfo jarFile, string iconEntryName)
    {
        var hashInput = $"{jarFile.FullName}|{jarFile.Length}|{jarFile.LastWriteTimeUtc.Ticks}|{iconEntryName}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(iconCacheDirectory, $"{hash}.png");
    }

    private static MetadataDeclaration? TryFindMetadataDeclaration(ZipArchive archive)
    {
        return TryFindFabricMetadataDeclaration(archive)
               ?? TryFindQuiltMetadataDeclaration(archive)
               ?? TryFindNeoForgeTomlMetadataDeclaration(archive)
               ?? TryFindForgeTomlMetadataDeclaration(archive)
               ?? TryFindMcmodInfoMetadataDeclaration(archive);
    }

    private static MetadataDeclaration? TryFindFabricMetadataDeclaration(ZipArchive archive)
    {
        var entry = archive.GetEntry("fabric.mod.json");
        if (entry is null)
            return null;

        var root = ParseJsonEntry(entry);
        var displayName = TryReadJsonString(root?["name"]);
        var modId = TryReadJsonString(root?["id"]);
        var version = TryReadJsonString(root?["version"]);
        var iconPath = TryReadJsonIconPath(root?["icon"]);
        return new MetadataDeclaration(entry.FullName, "fabric", displayName, modId, version, iconPath);
    }

    private static MetadataDeclaration? TryFindQuiltMetadataDeclaration(ZipArchive archive)
    {
        var entry = archive.GetEntry("quilt.mod.json");
        if (entry is null)
            return null;

        var root = ParseJsonEntry(entry);
        var displayName = TryReadJsonString(root?["quilt_loader"]?["metadata"]?["name"])
                          ?? TryReadJsonString(root?["quilt_loader"]?["name"]);
        var modId = TryReadJsonString(root?["quilt_loader"]?["id"])
                    ?? TryReadJsonString(root?["id"]);
        var version = TryReadJsonString(root?["quilt_loader"]?["version"])
                      ?? TryReadJsonString(root?["version"]);
        var iconPath = TryReadJsonIconPath(root?["quilt_loader"]?["icon"]);
        return new MetadataDeclaration(entry.FullName, "quilt", displayName, modId, version, iconPath);
    }

    private static MetadataDeclaration? TryFindNeoForgeTomlMetadataDeclaration(ZipArchive archive)
    {
        return TryFindTomlMetadataDeclaration(archive, "META-INF/neoforge.mods.toml", "neoforge");
    }

    private static MetadataDeclaration? TryFindForgeTomlMetadataDeclaration(ZipArchive archive)
    {
        return TryFindTomlMetadataDeclaration(archive, "META-INF/mods.toml", "forge");
    }

    private static MetadataDeclaration? TryFindTomlMetadataDeclaration(
        ZipArchive archive,
        string entryName,
        string loader)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
            return null;

        var content = ReadTextEntry(entry);
        var displayName = TryReadTomlValue(content, TomlDisplayNameRegex);
        var modId = TryReadTomlValue(content, TomlModIdRegex);
        var version = TryReadTomlValue(content, TomlVersionRegex);
        var iconPath = TryReadTomlValue(content, TomlLogoFileRegex);
        return new MetadataDeclaration(entry.FullName, loader, displayName, modId, version, iconPath);
    }

    private static MetadataDeclaration? TryFindMcmodInfoMetadataDeclaration(ZipArchive archive)
    {
        var entry = archive.GetEntry("mcmod.info");
        if (entry is null)
            return null;

        var root = ParseJsonEntry(entry);
        var displayName = FindFirstJsonString(root, "name", NormalizeDisplayName);
        var modId = FindFirstJsonString(root, "modid", NormalizeDisplayName);
        var version = FindFirstJsonString(root, "version", NormalizeDisplayName);
        var iconPath = FindFirstJsonString(root, "logoFile", NormalizeIconPath);
        return new MetadataDeclaration(entry.FullName, "forge", displayName, modId, version, iconPath);
    }

    private static JsonNode? ParseJsonEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonNode.Parse(stream);
    }

    private static string ReadTextEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string? TryReadJsonIconPath(JsonNode? node)
    {
        if (node is JsonValue stringValue && stringValue.TryGetValue<string>(out var iconPath))
            return NormalizeIconPath(iconPath);

        if (node is not JsonObject objectValue)
            return null;

        string? bestPath = null;
        var bestSize = int.MinValue;
        foreach (var property in objectValue)
        {
            if (!int.TryParse(property.Key, out var iconSize)
                || property.Value is not JsonValue candidateValue
                || !candidateValue.TryGetValue<string>(out var candidatePath))
            {
                continue;
            }

            var normalizedPath = NormalizeIconPath(candidatePath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || iconSize <= bestSize)
                continue;

            bestSize = iconSize;
            bestPath = normalizedPath;
        }

        return bestPath;
    }

    private static string? TryReadJsonString(JsonNode? node)
    {
        return node is JsonValue stringValue && stringValue.TryGetValue<string>(out var value)
            ? NormalizeDisplayName(value)
            : null;
    }

    private static string? TryReadTomlValue(string content, Regex regex)
    {
        var match = regex.Match(content);
        return match.Success
            ? NormalizeDisplayName(match.Groups["value"].Value)
            : null;
    }

    private static string? FindFirstJsonString(
        JsonNode? node,
        string propertyName,
        Func<string?, string?> normalize)
    {
        switch (node)
        {
            case JsonObject objectNode:
                if (objectNode[propertyName] is JsonValue value && value.TryGetValue<string>(out var direct))
                    return normalize(direct);

                foreach (var property in objectNode)
                {
                    var nested = FindFirstJsonString(property.Value, propertyName, normalize);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

                return null;
            case JsonArray arrayNode:
                foreach (var item in arrayNode)
                {
                    var nested = FindFirstJsonString(item, propertyName, normalize);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

                return null;
            default:
                return null;
        }
    }

    private static ZipArchiveEntry? TryResolveIconEntry(
        ZipArchive archive,
        string metadataEntryName,
        string declaredIconPath)
    {
        var normalizedPath = NormalizeIconPath(declaredIconPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return null;

        var metadataDirectory = GetArchiveDirectory(metadataEntryName);
        var candidates = new[]
        {
            normalizedPath,
            normalizedPath.TrimStart('/'),
            string.IsNullOrWhiteSpace(metadataDirectory)
                ? null
                : CombineArchivePath(metadataDirectory, normalizedPath.TrimStart('/'))
        };

        foreach (var candidate in candidates
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var match = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }

    private static string NormalizeIconPath(string? iconPath)
    {
        return string.IsNullOrWhiteSpace(iconPath)
            ? string.Empty
            : iconPath.Trim().Replace('\\', '/');
    }

    private static string? NormalizeDisplayName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string GetArchiveDirectory(string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalized[..separatorIndex];
    }

    private static string CombineArchivePath(string directory, string relativePath)
    {
        return string.IsNullOrWhiteSpace(directory)
            ? relativePath
            : $"{directory.TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    private static string GetModsDirectory(GameInstance instance) => Path.Combine(instance.InstanceDirectory, "mods");

    private static bool IsEnabledModPath(string path)
    {
        return path.EndsWith(EnabledModExtension, StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisabledModPath(string path)
    {
        if (path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase))
            return path;

        if (!path.EndsWith(EnabledModExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported mod path for disable: {path}");

        return path + ".disabled";
    }

    private static string GetEnabledModPath(string path)
    {
        if (path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase))
            return path[..^".disabled".Length];

        if (!path.EndsWith(EnabledModExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported mod path for enable: {path}");

        return path;
    }

    private static string GetDisplayFileNameWithoutModExtensions(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase))
            return fileName[..^DisabledModExtension.Length];

        if (fileName.EndsWith(EnabledModExtension, StringComparison.OrdinalIgnoreCase))
            return fileName[..^EnabledModExtension.Length];

        return Path.GetFileNameWithoutExtension(fileName);
    }

    private sealed record MetadataDeclaration(
        string MetadataEntryName,
        string? Loader,
        string? DisplayName,
        string? ModId,
        string? Version,
        string? IconPath);

    private sealed record ResolvedModMetadata(
        string? DisplayName,
        string? Loader,
        string? ModId,
        string? Version,
        string? IconSource)
    {
        public static readonly ResolvedModMetadata Empty = new(null, null, null, null, null);
    }
}
