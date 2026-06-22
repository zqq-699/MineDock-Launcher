using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class LocalModpackPackageService : IModpackPackageService
{
    private const string CurseForgeApiKeyEnvironmentVariable = "CURSEFORGE_API_KEY";
    private const string LocalSecretsDirectoryName = ".local-secrets";
    private const string CurseForgeApiKeyFileName = "curseforge.key";
    private readonly HttpClient httpClient;
    private readonly LauncherPathProvider pathProvider;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly CurseForgeApiClient curseForgeApiClient;
    private readonly ILogger<LocalModpackPackageService> logger;

    public LocalModpackPackageService(
        LauncherPathProvider pathProvider,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        HttpClient? httpClient = null,
        CurseForgeApiClient? curseForgeApiClient = null,
        ILogger<LocalModpackPackageService>? logger = null)
    {
        this.pathProvider = pathProvider;
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.httpClient = httpClient ?? new HttpClient();
        this.curseForgeApiClient = curseForgeApiClient ?? new CurseForgeApiClient(this.httpClient);
        this.logger = logger ?? NullLogger<LocalModpackPackageService>.Instance;
    }

    public async Task<ModpackRecognitionResult> RecognizeAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
            return ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.FileNotFound);

        try
        {
            return Path.GetExtension(normalizedArchivePath).ToLowerInvariant() switch
            {
                ".mrpack" => await RecognizeModrinthAsync(normalizedArchivePath, cancellationToken).ConfigureAwait(false),
                ".zip" => await RecognizeZipAsync(normalizedArchivePath, cancellationToken).ConfigureAwait(false),
                _ => ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.UnsupportedArchive)
            };
        }
        catch (ModpackImportException exception)
        {
            return ModpackRecognitionResult.Failure(MapRecognitionFailureReason(exception.FailureReason));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected modpack archive recognition failure. ArchivePath={ArchivePath}",
                normalizedArchivePath);
            return ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.UnexpectedError);
        }
    }

    public async Task<PreparedModpack> PrepareAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.FileNotFound,
                $"Modpack archive does not exist: {normalizedArchivePath}");
        }

        var workingDirectory = Path.Combine(
            pathProvider.DefaultDataDirectory,
            "cache",
            "modpacks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            logger.LogInformation(
                "Preparing local modpack archive. ArchivePath={ArchivePath} WorkingDirectory={WorkingDirectory}",
                normalizedArchivePath,
                workingDirectory);

            return Path.GetExtension(normalizedArchivePath).ToLowerInvariant() switch
            {
                ".mrpack" => await PrepareModrinthAsync(normalizedArchivePath, workingDirectory, cancellationToken).ConfigureAwait(false),
                ".zip" => await PrepareZipAsync(normalizedArchivePath, workingDirectory, cancellationToken).ConfigureAwait(false),
                _ => throw new ModpackImportException(
                    ModpackImportFailureReason.UnsupportedArchive,
                    $"Unsupported modpack archive type: {normalizedArchivePath}")
            };
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    public async Task InstallContentAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        ArgumentNullException.ThrowIfNull(preparedModpack);
        ArgumentNullException.ThrowIfNull(instance);

        var completedCount = 0;
        var totalCount = preparedModpack.Files.Count;
        foreach (var file in preparedModpack.Files)
        {
            progress?.Report(new LauncherProgress(
                ImportProgressStages.DownloadingPackFiles,
                file.FileName,
                totalCount == 0 ? null : completedCount * 100d / totalCount));

            await DownloadPackFileAsync(
                preparedModpack,
                file,
                instance,
                downloadSpeedLimitMbPerSecond,
                cancellationToken).ConfigureAwait(false);
            completedCount++;
        }

        if (!string.IsNullOrWhiteSpace(preparedModpack.OverridesDirectory)
            && Directory.Exists(preparedModpack.OverridesDirectory))
        {
            progress?.Report(new LauncherProgress(ImportProgressStages.CopyingOverrides, string.Empty));
            CopyOverrides(preparedModpack.OverridesDirectory, instance.InstanceDirectory);
        }
    }

    public Task CleanupAsync(
        PreparedModpack preparedModpack,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedModpack);

        return Task.Run(
            () => TryDeleteDirectory(preparedModpack.WorkingDirectory),
            cancellationToken);
    }

    private async Task<ModpackRecognitionResult> RecognizeModrinthAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        await ValidateModrinthArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
        return ModpackRecognitionResult.Success();
    }

    private async Task<ModpackRecognitionResult> RecognizeZipAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "manifest.json",
                StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is not null)
        {
            await ValidateCurseForgeArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
            return ModpackRecognitionResult.Success();
        }

        var embeddedMrpackEntries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name)
                && entry.Name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (embeddedMrpackEntries.Count == 1)
        {
            await ValidateEmbeddedModrinthAsync(embeddedMrpackEntries[0], cancellationToken).ConfigureAwait(false);
            return ModpackRecognitionResult.Success();
        }

        if (embeddedMrpackEntries.Count > 1)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Multiple embedded .mrpack files were found.");
        }

        return ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.InvalidManifest);
    }

    private async Task<PreparedModpack> PrepareModrinthAsync(
        string archivePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return await PrepareModrinthArchiveAsync(
            archive,
            archivePath,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedModpack> PrepareZipAsync(
        string archivePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "manifest.json",
                StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is not null)
        {
            return await PrepareCurseForgeArchiveAsync(
                archive,
                archivePath,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
        }

        var embeddedMrpackEntries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name)
                && entry.Name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (embeddedMrpackEntries.Count == 1)
        {
            logger.LogInformation(
                "Falling back to embedded Modrinth archive inside zip wrapper. ArchivePath={ArchivePath} EmbeddedEntry={EmbeddedEntry}",
                archivePath,
                embeddedMrpackEntries[0].FullName);
            return await PrepareEmbeddedModrinthAsync(
                embeddedMrpackEntries[0],
                archivePath,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
        }

        if (embeddedMrpackEntries.Count > 1)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Multiple embedded .mrpack files were found.");
        }

        throw new ModpackImportException(
            ModpackImportFailureReason.InvalidManifest,
            "manifest.json was not found.");
    }

    private async Task<PreparedModpack> PrepareEmbeddedModrinthAsync(
        ZipArchiveEntry mrpackEntry,
        string sourceArchivePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var extractedMrpackPath = Path.Combine(workingDirectory, "embedded", Path.GetFileName(mrpackEntry.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(extractedMrpackPath)!);
        await using (var source = mrpackEntry.Open())
        await using (var destination = new FileStream(extractedMrpackPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        using var stream = File.OpenRead(extractedMrpackPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return await PrepareModrinthArchiveAsync(
            archive,
            sourceArchivePath,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateEmbeddedModrinthAsync(
        ZipArchiveEntry mrpackEntry,
        CancellationToken cancellationToken)
    {
        await using var source = mrpackEntry.Open();
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
        await ValidateModrinthArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateModrinthArchiveAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var indexEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "modrinth.index.json",
                StringComparison.OrdinalIgnoreCase));
        if (indexEntry is null)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "modrinth.index.json was not found.");
        }

        var index = await ReadJsonDocumentAsync(indexEntry, cancellationToken).ConfigureAwait(false);
        try
        {
            var dependencies = GetRequiredObject(index.RootElement, "dependencies");
            _ = GetRequiredString(dependencies, "minecraft");
            _ = ParseModrinthLoader(dependencies);
            _ = ParseModrinthFiles(index.RootElement);
        }
        finally
        {
            index.Dispose();
        }
    }

    private async Task<PreparedModpack> PrepareModrinthArchiveAsync(
        ZipArchive archive,
        string sourceArchivePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var indexEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "modrinth.index.json",
                StringComparison.OrdinalIgnoreCase));
        if (indexEntry is null)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "modrinth.index.json was not found.");
        }

        var index = await ReadJsonDocumentAsync(indexEntry, cancellationToken).ConfigureAwait(false);
        try
        {
            var packageName = GetString(index.RootElement, "name");
            var dependencies = GetRequiredObject(index.RootElement, "dependencies");
            var minecraftVersion = GetRequiredString(dependencies, "minecraft");
            var (loader, loaderVersion) = ParseModrinthLoader(dependencies);
            var downloads = ParseModrinthFiles(index.RootElement);
            var overridesDirectory = ExtractModrinthOverrides(archive, workingDirectory, cancellationToken);

            return new PreparedModpack
            {
                PackageKind = ModpackPackageKind.Modrinth,
                SourceArchivePath = sourceArchivePath,
                WorkingDirectory = workingDirectory,
                PackageName = string.IsNullOrWhiteSpace(packageName)
                    ? Path.GetFileNameWithoutExtension(sourceArchivePath)
                    : packageName,
                MinecraftVersion = minecraftVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
                OverridesDirectory = overridesDirectory,
                Files = downloads
            };
        }
        finally
        {
            index.Dispose();
        }
    }

    private async Task ValidateCurseForgeArchiveAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "manifest.json",
                StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "manifest.json was not found.");
        }

        var manifest = await ReadJsonDocumentAsync(manifestEntry, cancellationToken).ConfigureAwait(false);
        try
        {
            var minecraft = GetRequiredObject(manifest.RootElement, "minecraft");
            _ = GetRequiredString(minecraft, "version");
            _ = ParseCurseForgeLoader(minecraft);
            ValidateCurseForgeFileEntries(manifest.RootElement);
        }
        finally
        {
            manifest.Dispose();
        }
    }

    private async Task<PreparedModpack> PrepareCurseForgeArchiveAsync(
        ZipArchive archive,
        string sourceArchivePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                ModpackArchiveUtility.NormalizeArchivePath(entry.FullName),
                "manifest.json",
                StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "manifest.json was not found.");
        }

        var manifest = await ReadJsonDocumentAsync(manifestEntry, cancellationToken).ConfigureAwait(false);
        try
        {
            var packageName = GetString(manifest.RootElement, "name");
            var minecraft = GetRequiredObject(manifest.RootElement, "minecraft");
            var minecraftVersion = GetRequiredString(minecraft, "version");
            var (loader, loaderVersion) = ParseCurseForgeLoader(minecraft);
            var downloads = await ParseCurseForgeFilesAsync(manifest.RootElement, cancellationToken).ConfigureAwait(false);
            var overridesDirectory = ExtractCurseForgeOverrides(archive, workingDirectory, cancellationToken);

            return new PreparedModpack
            {
                PackageKind = ModpackPackageKind.CurseForge,
                SourceArchivePath = sourceArchivePath,
                WorkingDirectory = workingDirectory,
                PackageName = string.IsNullOrWhiteSpace(packageName)
                    ? Path.GetFileNameWithoutExtension(sourceArchivePath)
                    : packageName,
                MinecraftVersion = minecraftVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
                OverridesDirectory = overridesDirectory,
                Files = downloads
            };
        }
        finally
        {
            manifest.Dispose();
        }
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<PreparedModpackDownload> ParseModrinthFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return [];

        var downloads = new List<PreparedModpackDownload>();
        foreach (var file in files.EnumerateArray())
        {
            if (ShouldSkipClientFile(file))
                continue;

            var relativePath = GetRequiredString(file, "path");
            var sourceUrl = ResolveModrinthDownloadUrl(file);
            var hashes = GetRequiredObject(file, "hashes");
            var sha1 = GetString(hashes, "sha1");
            var sha512 = GetString(hashes, "sha512");
            if (string.IsNullOrWhiteSpace(sha1) && string.IsNullOrWhiteSpace(sha512))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    $"Modrinth file is missing supported hashes: {relativePath}");
            }

            downloads.Add(new PreparedModpackDownload
            {
                FileName = Path.GetFileName(relativePath),
                RelativePath = relativePath,
                SourceUrl = sourceUrl,
                Sha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1,
                Sha512 = string.IsNullOrWhiteSpace(sha512) ? null : sha512
            });
        }

        return downloads;
    }

    private static void ValidateCurseForgeFileEntries(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return;

        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("projectID", out var projectIdProperty)
                || !projectIdProperty.TryGetInt64(out _)
                || !file.TryGetProperty("fileID", out var fileIdProperty)
                || !fileIdProperty.TryGetInt64(out _))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    "CurseForge manifest file entry is missing projectID or fileID.");
            }
        }
    }

    private async Task<IReadOnlyList<PreparedModpackDownload>> ParseCurseForgeFilesAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return [];

        string? apiKey = null;
        var downloads = new List<PreparedModpackDownload>();
        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("projectID", out var projectIdProperty)
                || !projectIdProperty.TryGetInt64(out var projectId)
                || !file.TryGetProperty("fileID", out var fileIdProperty)
                || !fileIdProperty.TryGetInt64(out var fileId))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    "CurseForge manifest file entry is missing projectID or fileID.");
            }

            apiKey ??= GetCurseForgeApiKey();
            var resolvedFile = await curseForgeApiClient
                .GetFileDownloadAsync(projectId, fileId, apiKey, cancellationToken)
                .ConfigureAwait(false);
            downloads.Add(new PreparedModpackDownload
            {
                FileName = resolvedFile.FileName,
                RelativePath = $"mods/{resolvedFile.FileName}",
                SourceUrl = resolvedFile.DownloadUrl
            });
        }

        return downloads;
    }

    private static string? ExtractModrinthOverrides(
        ZipArchive archive,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var extractedAny = false;
        var overridesDirectory = Path.Combine(workingDirectory, "overrides");

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var normalizedPath = ModpackArchiveUtility.NormalizeArchivePath(entry.FullName);
            var relativePath = ModpackArchiveUtility.RemovePrefix(normalizedPath, "overrides");
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                ModpackArchiveUtility.ExtractZipEntry(entry, overridesDirectory, relativePath, cancellationToken);
                extractedAny = true;
                continue;
            }

            relativePath = ModpackArchiveUtility.RemovePrefix(normalizedPath, "client-overrides");
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            ModpackArchiveUtility.ExtractZipEntry(entry, overridesDirectory, relativePath, cancellationToken);
            extractedAny = true;
        }

        return extractedAny ? overridesDirectory : null;
    }

    private static string? ExtractCurseForgeOverrides(
        ZipArchive archive,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var extractedAny = false;
        var overridesDirectory = Path.Combine(workingDirectory, "overrides");

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var normalizedPath = ModpackArchiveUtility.NormalizeArchivePath(entry.FullName);
            var relativePath = ModpackArchiveUtility.RemovePrefix(normalizedPath, "overrides");
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            ModpackArchiveUtility.ExtractZipEntry(entry, overridesDirectory, relativePath, cancellationToken);
            extractedAny = true;
        }

        return extractedAny ? overridesDirectory : null;
    }

    private async Task DownloadPackFileAsync(
        PreparedModpack preparedModpack,
        PreparedModpackDownload file,
        GameInstance instance,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        if (!ModpackArchiveUtility.IsSupportedHttpUrl(file.SourceUrl))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"Unsupported download URL: {file.SourceUrl}");
        }

        var targetPath = ModpackArchiveUtility.GetValidatedTargetPath(instance.InstanceDirectory, file.RelativePath);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        var downloadDirectory = Path.Combine(preparedModpack.WorkingDirectory, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var tempFilePath = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N"));

        try
        {
            using var response = await httpClient.GetAsync(
                file.SourceUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var bandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await CopyWithThrottleAsync(source, destination, bandwidthLimiter, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(file.Sha512))
                await VerifyHashAsync(tempFilePath, file.Sha512, HashAlgorithmName.SHA512, cancellationToken).ConfigureAwait(false);
            else if (!string.IsNullOrWhiteSpace(file.Sha1))
                await VerifyHashAsync(tempFilePath, file.Sha1, HashAlgorithmName.SHA1, cancellationToken).ConfigureAwait(false);

            File.Move(tempFilePath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    private static async Task CopyWithThrottleAsync(
        Stream source,
        Stream destination,
        DownloadBandwidthLimiter? bandwidthLimiter,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            if (bandwidthLimiter is not null)
                await bandwidthLimiter.ThrottleAsync(read, cancellationToken).ConfigureAwait(false);

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task VerifyHashAsync(
        string filePath,
        string expectedHash,
        HashAlgorithmName algorithmName,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var actualHashBytes = algorithmName.Name switch
        {
            "SHA1" => await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false),
            "SHA512" => await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported hash algorithm: {algorithmName.Name}")
        };

        var actualHash = Convert.ToHexString(actualHashBytes).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.HashMismatch,
                $"Hash mismatch for {filePath}.");
        }
    }

    private static void CopyOverrides(string sourceDirectory, string instanceDirectory)
    {
        foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var destinationPath = ModpackArchiveUtility.GetValidatedTargetPath(instanceDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            File.Copy(sourceFilePath, destinationPath, overwrite: true);
        }
    }

    private string GetCurseForgeApiKey()
    {
        var fileApiKey = TryReadCurseForgeApiKeyFromLocalSecretFile();
        if (!string.IsNullOrWhiteSpace(fileApiKey))
            return fileApiKey;

        var apiKey = Environment.GetEnvironmentVariable(CurseForgeApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogInformation(
                "Resolved CurseForge API key from environment variable. VariableName={VariableName}",
                CurseForgeApiKeyEnvironmentVariable);
            return apiKey.Trim();
        }

        throw new ModpackImportException(
            ModpackImportFailureReason.MissingCurseForgeApiKey,
            "CurseForge API key was not configured.");
    }

    private string? TryReadCurseForgeApiKeyFromLocalSecretFile()
    {
        foreach (var root in EnumerateCurseForgeSecretSearchRoots())
        {
            foreach (var directory in EnumerateDirectoryAndAncestors(root))
            {
                var keyPath = Path.Combine(directory, LocalSecretsDirectoryName, CurseForgeApiKeyFileName);
                try
                {
                    if (!File.Exists(keyPath))
                        continue;

                    var value = File.ReadAllText(keyPath).Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        logger.LogWarning(
                            "Ignored empty CurseForge API key file. KeyPath={KeyPath}",
                            keyPath);
                        continue;
                    }

                    logger.LogInformation(
                        "Resolved CurseForge API key from local secret file. KeyPath={KeyPath}",
                        keyPath);
                    return value;
                }
                catch (IOException exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to read local CurseForge API key file. KeyPath={KeyPath}",
                        keyPath);
                }
                catch (UnauthorizedAccessException exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to access local CurseForge API key file. KeyPath={KeyPath}",
                        keyPath);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCurseForgeSecretSearchRoots()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(currentDirectory))
            yield return Path.GetFullPath(currentDirectory);
    }

    private static IEnumerable<string> EnumerateDirectoryAndAncestors(string startDirectory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
             current is not null;
             current = current.Parent)
        {
            yield return current.FullName;
        }
    }

    private static (LoaderKind Loader, string? LoaderVersion) ParseModrinthLoader(JsonElement dependencies)
    {
        var loaderEntries = new List<(LoaderKind Loader, string? LoaderVersion)>();
        if (TryGetString(dependencies, "fabric-loader", out var fabricVersion))
            loaderEntries.Add((LoaderKind.Fabric, fabricVersion));
        if (TryGetString(dependencies, "forge", out var forgeVersion))
            loaderEntries.Add((LoaderKind.Forge, forgeVersion));
        if (TryGetString(dependencies, "neoforge", out _))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.UnsupportedLoader,
                "NeoForge modpacks are not supported.");
        }

        if (TryGetString(dependencies, "quilt-loader", out _))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.UnsupportedLoader,
                "Quilt modpacks are not supported.");
        }

        return loaderEntries.Count switch
        {
            0 => (LoaderKind.Vanilla, null),
            1 => loaderEntries[0],
            _ => throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Modrinth modpack declares multiple loaders.")
        };
    }

    private static (LoaderKind Loader, string? LoaderVersion) ParseCurseForgeLoader(JsonElement minecraft)
    {
        if (!minecraft.TryGetProperty("modLoaders", out var modLoaders)
            || modLoaders.ValueKind != JsonValueKind.Array
            || modLoaders.GetArrayLength() == 0)
        {
            return (LoaderKind.Vanilla, null);
        }

        JsonElement? selected = null;
        foreach (var modLoader in modLoaders.EnumerateArray())
        {
            if (modLoader.TryGetProperty("primary", out var primaryProperty)
                && primaryProperty.ValueKind == JsonValueKind.True)
            {
                selected = modLoader;
                break;
            }

            selected ??= modLoader;
        }

        if (selected is null)
            return (LoaderKind.Vanilla, null);

        var id = GetRequiredString(selected.Value, "id");
        if (id.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.Forge, id["forge-".Length..]);
        if (id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.Fabric, id["fabric-".Length..]);
        if (id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.UnsupportedLoader,
                $"Unsupported CurseForge loader: {id}");
        }

        throw new ModpackImportException(
            ModpackImportFailureReason.UnsupportedLoader,
            $"Unsupported CurseForge loader: {id}");
    }

    private static bool ShouldSkipClientFile(JsonElement file)
    {
        if (!file.TryGetProperty("env", out var env)
            || env.ValueKind != JsonValueKind.Object
            || !env.TryGetProperty("client", out var clientProperty)
            || clientProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(clientProperty.GetString(), "unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveModrinthDownloadUrl(JsonElement file)
    {
        if (!file.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Array)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Modrinth file entry is missing downloads.");
        }

        foreach (var download in downloads.EnumerateArray())
        {
            if (download.ValueKind != JsonValueKind.String)
                continue;

            var url = download.GetString();
            if (!string.IsNullOrWhiteSpace(url) && ModpackArchiveUtility.IsSupportedHttpUrl(url))
                return url;
        }

        throw new ModpackImportException(
            ModpackImportFailureReason.InvalidManifest,
            "Modrinth file entry does not contain a supported download URL.");
    }

    private static JsonElement GetRequiredObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"Required object property '{propertyName}' is missing.");
        }

        return property;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (TryGetString(element, propertyName, out var value))
            return value;

        throw new ModpackImportException(
            ModpackImportFailureReason.InvalidManifest,
            $"Required string property '{propertyName}' is missing.");
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value)
            ? value
            : string.Empty;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static ModpackRecognitionFailureReason MapRecognitionFailureReason(ModpackImportFailureReason failureReason)
    {
        return failureReason switch
        {
            ModpackImportFailureReason.FileNotFound => ModpackRecognitionFailureReason.FileNotFound,
            ModpackImportFailureReason.UnsupportedArchive => ModpackRecognitionFailureReason.UnsupportedArchive,
            ModpackImportFailureReason.InvalidManifest => ModpackRecognitionFailureReason.InvalidManifest,
            ModpackImportFailureReason.UnsupportedLoader => ModpackRecognitionFailureReason.UnsupportedLoader,
            _ => ModpackRecognitionFailureReason.UnexpectedError
        };
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
