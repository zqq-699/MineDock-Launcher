using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class ModpackExportService : IModpackExportService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IModService modService;
    private readonly ILocalResourcePackService resourcePackService;
    private readonly ILocalShaderPackService shaderPackService;
    private readonly ICurseForgeApiKeyResolver curseForgeApiKeyResolver;
    private readonly CurseForgeApiClient curseForgeApiClient;
    private readonly ILogger<ModpackExportService> logger;

    public ModpackExportService(
        IModService modService,
        ILocalResourcePackService resourcePackService,
        ILocalShaderPackService shaderPackService,
        ICurseForgeApiKeyResolver curseForgeApiKeyResolver,
        CurseForgeApiClient? curseForgeApiClient = null,
        ILogger<ModpackExportService>? logger = null)
    {
        this.modService = modService;
        this.resourcePackService = resourcePackService;
        this.shaderPackService = shaderPackService;
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver;
        this.curseForgeApiClient = curseForgeApiClient ?? new CurseForgeApiClient();
        this.logger = logger ?? NullLogger<ModpackExportService>.Instance;
    }

    public async Task<ModpackExportResult> ExportAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Kind is not ModpackExportKind.CurseForge)
            return Failure(ModpackExportFailureReason.UnsupportedType);

        var validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
            return validationFailure;

        logger.LogInformation(
            "CurseForge modpack export started. InstanceId={InstanceId} OutputArchivePath={OutputArchivePath} IncludeMods={IncludeMods} IncludeDisabledMods={IncludeDisabledMods} IncludeResourcePacks={IncludeResourcePacks} IncludeShaderPacks={IncludeShaderPacks} IncludeConfig={IncludeConfig}",
            request.Instance.Id,
            request.OutputArchivePath,
            request.IncludeMods,
            request.IncludeDisabledMods,
            request.IncludeResourcePacks,
            request.IncludeShaderPacks,
            request.IncludeConfig);

        try
        {
            var candidates = await CollectCandidatesAsync(request, cancellationToken).ConfigureAwait(false);
            var fingerprintMatches = await ResolveFingerprintMatchesAsync(candidates, cancellationToken)
                .ConfigureAwait(false);

            var manifestFiles = new List<CurseForgeManifestFile>();
            var manifestFileKeys = new HashSet<string>(StringComparer.Ordinal);
            var overrideFiles = new List<OverrideFile>();
            foreach (var candidate in candidates)
            {
                if (candidate.Fingerprint is { } fingerprint
                    && fingerprintMatches.TryGetValue(fingerprint, out var match)
                    && manifestFileKeys.Add($"{match.ProjectId}:{match.FileId}"))
                {
                    manifestFiles.Add(new CurseForgeManifestFile(match.ProjectId, match.FileId, true));
                    continue;
                }

                overrideFiles.Add(new OverrideFile(candidate.SourcePath, candidate.OverridePath));
            }

            if (request.IncludeConfig)
                overrideFiles.AddRange(EnumerateConfigFiles(request.Instance.InstanceDirectory));

            var manifest = CreateCurseForgeManifest(request, manifestFiles);
            var outputPath = Path.GetFullPath(request.OutputArchivePath);
            var tempPath = CreateTempArchivePath(outputPath);
            try
            {
                await WriteArchiveAsync(tempPath, manifest, overrideFiles, cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }

            logger.LogInformation(
                "CurseForge modpack export completed. InstanceId={InstanceId} OutputArchivePath={OutputArchivePath} ManifestFileCount={ManifestFileCount} OverrideFileCount={OverrideFileCount}",
                request.Instance.Id,
                outputPath,
                manifestFiles.Count,
                overrideFiles.Count);

            return new ModpackExportResult(
                true,
                OutputArchivePath: outputPath,
                ManifestFileCount: manifestFiles.Count,
                OverrideFileCount: overrideFiles.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MissingCurseForgeApiKeyException)
        {
            return Failure(ModpackExportFailureReason.MissingCurseForgeApiKey);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "CurseForge modpack export API request failed. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.CurseForgeApiFailed);
        }
        catch (IOException exception)
        {
            logger.LogError(
                exception,
                "CurseForge modpack export file operation failed. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.FileSystemError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogError(
                exception,
                "CurseForge modpack export file access failed. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.FileSystemError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "CurseForge modpack export failed unexpectedly. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.UnexpectedError);
        }
    }

    private static ModpackExportResult? ValidateRequest(ModpackExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Version)
            || string.IsNullOrWhiteSpace(request.OutputArchivePath)
            || string.IsNullOrWhiteSpace(request.Instance.MinecraftVersion)
            || string.IsNullOrWhiteSpace(request.Instance.InstanceDirectory)
            || !Directory.Exists(request.Instance.InstanceDirectory))
        {
            return Failure(ModpackExportFailureReason.InvalidRequest);
        }

        if (request.Instance.Loader is not LoaderKind.Vanilla
            && string.IsNullOrWhiteSpace(request.Instance.LoaderVersion))
        {
            return Failure(ModpackExportFailureReason.MissingLoaderVersion);
        }

        return null;
    }

    private async Task<IReadOnlyList<ExportFileCandidate>> CollectCandidatesAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ExportFileCandidate>();
        if (request.IncludeMods)
        {
            var mods = await modService.GetModsAsync(request.Instance, cancellationToken).ConfigureAwait(false);
            foreach (var mod in mods.Where(mod => mod.IsEnabled))
                candidates.Add(await CreateCandidateAsync(mod.FullPath, "mods", cancellationToken).ConfigureAwait(false));

            if (request.IncludeDisabledMods)
            {
                foreach (var mod in mods.Where(mod => !mod.IsEnabled))
                    candidates.Add(CreateOverrideOnlyCandidate(mod.FullPath, "mods"));
            }
        }

        if (request.IncludeResourcePacks)
        {
            var resourcePacks = await resourcePackService.GetResourcePacksAsync(request.Instance, cancellationToken)
                .ConfigureAwait(false);
            foreach (var resourcePack in resourcePacks)
                candidates.Add(await CreateCandidateAsync(resourcePack.FullPath, "resourcepacks", cancellationToken).ConfigureAwait(false));
        }

        if (request.IncludeShaderPacks)
        {
            var shaderPacks = await shaderPackService.GetShaderPacksAsync(request.Instance, cancellationToken)
                .ConfigureAwait(false);
            foreach (var shaderPack in shaderPacks)
                candidates.Add(await CreateCandidateAsync(shaderPack.FullPath, "shaderpacks", cancellationToken).ConfigureAwait(false));
        }

        return candidates
            .Where(candidate => File.Exists(candidate.SourcePath))
            .ToArray();
    }

    private async Task<ExportFileCandidate> CreateCandidateAsync(
        string sourcePath,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(sourcePath);
        var fileName = Path.GetFileName(normalizedPath);
        long? fingerprint = null;
        try
        {
            fingerprint = await CurseForgeFingerprintUtility.ComputeFileFingerprintAsync(normalizedPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "Failed to fingerprint export file; it will be written to overrides. FilePath={FilePath}",
                normalizedPath);
        }

        return new ExportFileCandidate(
            normalizedPath,
            $"{targetDirectory}/{fileName}",
            fingerprint);
    }

    private static ExportFileCandidate CreateOverrideOnlyCandidate(
        string sourcePath,
        string targetDirectory)
    {
        var normalizedPath = Path.GetFullPath(sourcePath);
        var fileName = Path.GetFileName(normalizedPath);
        return new ExportFileCandidate(
            normalizedPath,
            $"{targetDirectory}/{fileName}",
            null);
    }

    private async Task<IReadOnlyDictionary<long, CurseForgeApiClient.CurseForgeFingerprintMatch>> ResolveFingerprintMatchesAsync(
        IReadOnlyList<ExportFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var fingerprints = candidates
            .Select(candidate => candidate.Fingerprint)
            .OfType<long>()
            .Distinct()
            .ToArray();
        if (fingerprints.Length == 0)
            return new Dictionary<long, CurseForgeApiClient.CurseForgeFingerprintMatch>();

        var apiKey = await curseForgeApiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("CurseForge modpack export could not start because API key is not configured.");
            throw new MissingCurseForgeApiKeyException();
        }

        return await curseForgeApiClient.GetFingerprintMatchesAsync(fingerprints, apiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private static CurseForgeManifest CreateCurseForgeManifest(
        ModpackExportRequest request,
        IReadOnlyList<CurseForgeManifestFile> files)
    {
        var modLoaders = new List<CurseForgeModLoader>();
        if (request.Instance.Loader is not LoaderKind.Vanilla)
        {
            modLoaders.Add(new CurseForgeModLoader(
                $"{ResolveCurseForgeLoaderId(request.Instance.Loader)}-{request.Instance.LoaderVersion!.Trim()}",
                true));
        }

        return new CurseForgeManifest(
            new CurseForgeMinecraft(request.Instance.MinecraftVersion.Trim(), modLoaders),
            "minecraftModpack",
            1,
            request.Name.Trim(),
            request.Version.Trim(),
            request.Author.Trim(),
            files,
            "overrides");
    }

    private static string ResolveCurseForgeLoaderId(LoaderKind loader)
    {
        return loader switch
        {
            LoaderKind.Forge => "forge",
            LoaderKind.Fabric => "fabric",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt",
            _ => throw new InvalidOperationException($"Unsupported CurseForge loader: {loader}")
        };
    }

    private static IEnumerable<OverrideFile> EnumerateConfigFiles(string instanceDirectory)
    {
        var configDirectory = Path.Combine(instanceDirectory, "config");
        if (!Directory.Exists(configDirectory))
            yield break;

        foreach (var filePath in Directory.EnumerateFiles(configDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(configDirectory, filePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("../", StringComparison.Ordinal))
                continue;

            yield return new OverrideFile(Path.GetFullPath(filePath), $"config/{relativePath}");
        }
    }

    private static async Task WriteArchiveAsync(
        string archivePath,
        CurseForgeManifest manifest,
        IReadOnlyList<OverrideFile> overrideFiles,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                    manifestStream,
                    manifest,
                    ManifestJsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var file in overrideFiles)
            await AddOverrideFileAsync(archive, file, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddOverrideFileAsync(
        ZipArchive archive,
        OverrideFile file,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(file.SourcePath))
            return;

        var entryName = $"overrides/{file.RelativePath}"
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var source = new FileStream(
            file.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTempArchivePath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var fileName = Path.GetFileName(outputPath);
        return Path.Combine(directory!, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ModpackExportResult Failure(ModpackExportFailureReason reason)
    {
        return new ModpackExportResult(false, reason);
    }

    private sealed record ExportFileCandidate(string SourcePath, string OverridePath, long? Fingerprint);

    private sealed record OverrideFile(string SourcePath, string RelativePath);

    private sealed record CurseForgeManifest(
        [property: JsonPropertyName("minecraft")] CurseForgeMinecraft Minecraft,
        [property: JsonPropertyName("manifestType")] string ManifestType,
        [property: JsonPropertyName("manifestVersion")] int ManifestVersion,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("files")] IReadOnlyList<CurseForgeManifestFile> Files,
        [property: JsonPropertyName("overrides")] string Overrides);

    private sealed record CurseForgeMinecraft(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("modLoaders")] IReadOnlyList<CurseForgeModLoader> ModLoaders);

    private sealed record CurseForgeModLoader(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("primary")] bool Primary);

    private sealed record CurseForgeManifestFile(
        [property: JsonPropertyName("projectID")] long ProjectId,
        [property: JsonPropertyName("fileID")] long FileId,
        [property: JsonPropertyName("required")] bool Required);

    private sealed class MissingCurseForgeApiKeyException : Exception;
}
