using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
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
    private readonly ModrinthApiClient modrinthApiClient;
    private readonly ILogger<ModpackExportService> logger;

    public ModpackExportService(
        IModService modService,
        ILocalResourcePackService resourcePackService,
        ILocalShaderPackService shaderPackService,
        ICurseForgeApiKeyResolver curseForgeApiKeyResolver,
        CurseForgeApiClient? curseForgeApiClient = null,
        ModrinthApiClient? modrinthApiClient = null,
        ILogger<ModpackExportService>? logger = null)
    {
        this.modService = modService;
        this.resourcePackService = resourcePackService;
        this.shaderPackService = shaderPackService;
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver;
        this.curseForgeApiClient = curseForgeApiClient ?? new CurseForgeApiClient();
        this.modrinthApiClient = modrinthApiClient ?? new ModrinthApiClient();
        this.logger = logger ?? NullLogger<ModpackExportService>.Instance;
    }

    public async Task<ModpackExportResult> ExportAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
            return validationFailure;

        return request.Kind switch
        {
            ModpackExportKind.CurseForge => await ExportCurseForgeAsync(request, cancellationToken).ConfigureAwait(false),
            ModpackExportKind.Modrinth => await ExportModrinthAsync(request, cancellationToken).ConfigureAwait(false),
            _ => Failure(ModpackExportFailureReason.UnsupportedType)
        };
    }

    private async Task<ModpackExportResult> ExportCurseForgeAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken)
    {
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
                await WriteCurseForgeArchiveAsync(tempPath, manifest, overrideFiles, cancellationToken)
                    .ConfigureAwait(false);
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

    private async Task<ModpackExportResult> ExportModrinthAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Modrinth modpack export started. InstanceId={InstanceId} OutputArchivePath={OutputArchivePath} IncludeMods={IncludeMods} IncludeDisabledMods={IncludeDisabledMods} IncludeResourcePacks={IncludeResourcePacks} IncludeShaderPacks={IncludeShaderPacks} IncludeConfig={IncludeConfig}",
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
            var resolvedCandidates = await CreateModrinthCandidatesAsync(candidates, cancellationToken)
                .ConfigureAwait(false);
            var matches = await ResolveModrinthMatchesAsync(resolvedCandidates, cancellationToken)
                .ConfigureAwait(false);

            var manifestFiles = new List<ModrinthManifestFile>();
            var manifestFileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var overrideFiles = new List<OverrideFile>();
            foreach (var candidate in resolvedCandidates)
            {
                if (!candidate.IsOverrideOnly
                    && candidate.Sha1 is { } sha1
                    && matches.TryGetValue(sha1, out var match)
                    && !string.IsNullOrWhiteSpace(match.Url)
                    && manifestFileKeys.Add(candidate.OverridePath))
                {
                    manifestFiles.Add(new ModrinthManifestFile(
                        candidate.OverridePath,
                        new ModrinthManifestHashes(
                            candidate.Sha1,
                            string.IsNullOrWhiteSpace(candidate.Sha512) ? match.Sha512 : candidate.Sha512),
                        new ModrinthManifestEnvironment("required", "unsupported"),
                        [match.Url],
                        candidate.SizeBytes ?? match.Size));
                    continue;
                }

                overrideFiles.Add(new OverrideFile(candidate.SourcePath, candidate.OverridePath));
            }

            if (request.IncludeConfig)
                overrideFiles.AddRange(EnumerateConfigFiles(request.Instance.InstanceDirectory));

            var manifest = CreateModrinthManifest(request, manifestFiles);
            var outputPath = Path.GetFullPath(request.OutputArchivePath);
            var tempPath = CreateTempArchivePath(outputPath);
            try
            {
                await WriteModrinthArchiveAsync(tempPath, manifest, overrideFiles, cancellationToken)
                    .ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }

            logger.LogInformation(
                "Modrinth modpack export completed. InstanceId={InstanceId} OutputArchivePath={OutputArchivePath} ManifestFileCount={ManifestFileCount} OverrideFileCount={OverrideFileCount}",
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
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Modrinth modpack export API request failed. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.ModrinthApiFailed);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Modrinth modpack export API response was invalid. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.ModrinthApiFailed);
        }
        catch (IOException exception)
        {
            logger.LogError(
                exception,
                "Modrinth modpack export file operation failed. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.FileSystemError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogError(
                exception,
                "Modrinth modpack export file access failed. InstanceId={InstanceId}",
                request.Instance.Id);
            return Failure(ModpackExportFailureReason.FileSystemError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Modrinth modpack export failed unexpectedly. InstanceId={InstanceId}",
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
            fingerprint,
            IsOverrideOnly: false);
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
            null,
            IsOverrideOnly: true);
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

    private async Task<IReadOnlyList<ModrinthExportFileCandidate>> CreateModrinthCandidatesAsync(
        IReadOnlyList<ExportFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var result = new List<ModrinthExportFileCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (candidate.IsOverrideOnly)
            {
                result.Add(new ModrinthExportFileCandidate(
                    candidate.SourcePath,
                    candidate.OverridePath,
                    IsOverrideOnly: true,
                    Sha1: null,
                    Sha512: null,
                    SizeBytes: null));
                continue;
            }

            try
            {
                var hashes = await ComputeModrinthHashesAsync(candidate.SourcePath, cancellationToken)
                    .ConfigureAwait(false);
                result.Add(new ModrinthExportFileCandidate(
                    candidate.SourcePath,
                    candidate.OverridePath,
                    IsOverrideOnly: false,
                    hashes.Sha1,
                    hashes.Sha512,
                    hashes.SizeBytes));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or ArgumentException
                or NotSupportedException)
            {
                logger.LogWarning(
                    exception,
                    "Failed to hash export file for Modrinth lookup; it will be written to overrides. FilePath={FilePath}",
                    candidate.SourcePath);
                result.Add(new ModrinthExportFileCandidate(
                    candidate.SourcePath,
                    candidate.OverridePath,
                    IsOverrideOnly: true,
                    Sha1: null,
                    Sha512: null,
                    SizeBytes: null));
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, ModrinthApiClient.ModrinthVersionFileMatch>> ResolveModrinthMatchesAsync(
        IReadOnlyList<ModrinthExportFileCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var hashes = candidates
            .Where(candidate => !candidate.IsOverrideOnly)
            .Select(candidate => candidate.Sha1)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hashes.Length == 0)
            return new Dictionary<string, ModrinthApiClient.ModrinthVersionFileMatch>(StringComparer.OrdinalIgnoreCase);

        return await modrinthApiClient.GetVersionFileMatchesAsync(hashes, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(string Sha1, string Sha512, long SizeBytes)> ComputeModrinthHashesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        using var sha1 = SHA1.Create();
        using var sha512 = SHA512.Create();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            sha1.TransformBlock(buffer, 0, read, null, 0);
            sha512.TransformBlock(buffer, 0, read, null, 0);
        }

        sha1.TransformFinalBlock([], 0, 0);
        sha512.TransformFinalBlock([], 0, 0);
        return (
            Convert.ToHexString(sha1.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha512.Hash!).ToLowerInvariant(),
            stream.Length);
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

    private static ModrinthManifest CreateModrinthManifest(
        ModpackExportRequest request,
        IReadOnlyList<ModrinthManifestFile> files)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["minecraft"] = request.Instance.MinecraftVersion.Trim()
        };
        if (request.Instance.Loader is not LoaderKind.Vanilla)
            dependencies[ResolveModrinthLoaderId(request.Instance.Loader)] = request.Instance.LoaderVersion!.Trim();

        return new ModrinthManifest(
            1,
            "minecraft",
            request.Version.Trim(),
            request.Name.Trim(),
            string.Empty,
            files,
            dependencies);
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

    private static string ResolveModrinthLoaderId(LoaderKind loader)
    {
        return loader switch
        {
            LoaderKind.Forge => "forge",
            LoaderKind.Fabric => "fabric-loader",
            LoaderKind.NeoForge => "neoforge",
            LoaderKind.Quilt => "quilt-loader",
            _ => throw new InvalidOperationException($"Unsupported Modrinth loader: {loader}")
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

    private static async Task WriteCurseForgeArchiveAsync(
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

    private static async Task WriteModrinthArchiveAsync(
        string archivePath,
        ModrinthManifest manifest,
        IReadOnlyList<OverrideFile> overrideFiles,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        var manifestEntry = archive.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
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

    private sealed record ExportFileCandidate(
        string SourcePath,
        string OverridePath,
        long? Fingerprint,
        bool IsOverrideOnly);

    private sealed record ModrinthExportFileCandidate(
        string SourcePath,
        string OverridePath,
        bool IsOverrideOnly,
        string? Sha1,
        string? Sha512,
        long? SizeBytes);

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

    private sealed record ModrinthManifest(
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("game")] string Game,
        [property: JsonPropertyName("versionId")] string VersionId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("files")] IReadOnlyList<ModrinthManifestFile> Files,
        [property: JsonPropertyName("dependencies")] IReadOnlyDictionary<string, string> Dependencies);

    private sealed record ModrinthManifestFile(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("hashes")] ModrinthManifestHashes Hashes,
        [property: JsonPropertyName("env")] ModrinthManifestEnvironment Environment,
        [property: JsonPropertyName("downloads")] IReadOnlyList<string> Downloads,
        [property: JsonPropertyName("fileSize")] long FileSize);

    private sealed record ModrinthManifestHashes(
        [property: JsonPropertyName("sha1")] string Sha1,
        [property: JsonPropertyName("sha512")] string? Sha512);

    private sealed record ModrinthManifestEnvironment(
        [property: JsonPropertyName("client")] string Client,
        [property: JsonPropertyName("server")] string Server);

    private sealed class MissingCurseForgeApiKeyException : Exception;
}
