using System.IO;
using System.IO.Compression;
using System.Net;
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
    private static readonly HashSet<string> CurseForgeDownloadHosts =
    [
        "api.curseforge.com",
        "edge.forgecdn.net",
        "mediafilez.forgecdn.net"
    ];
    private readonly HttpClient httpClient;
    private readonly LauncherPathProvider pathProvider;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly IImportConcurrencyLimiter limiter;
    private readonly CurseForgeApiClient curseForgeApiClient;
    private readonly ILogger<LocalModpackPackageService> logger;

    public LocalModpackPackageService(
        LauncherPathProvider pathProvider,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        IImportConcurrencyLimiter? limiter = null,
        HttpClient? httpClient = null,
        CurseForgeApiClient? curseForgeApiClient = null,
        ILogger<LocalModpackPackageService>? logger = null)
    {
        this.pathProvider = pathProvider;
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
        this.httpClient = httpClient ?? new HttpClient();
        this.curseForgeApiClient = curseForgeApiClient ?? new CurseForgeApiClient(this.httpClient, this.limiter);
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
        CancellationToken cancellationToken = default,
        IProgress<LauncherProgress>? progress = null)
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
                ".zip" => await PrepareZipAsync(normalizedArchivePath, workingDirectory, cancellationToken, progress).ConfigureAwait(false),
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

    public async Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        ArgumentNullException.ThrowIfNull(preparedModpack);
        ArgumentNullException.ThrowIfNull(instance);

        var curseForgeApiKey = preparedModpack.PackageKind is ModpackPackageKind.CurseForge
            ? GetCurseForgeApiKey()
            : null;
        var totalCount = preparedModpack.Files.Count;
        if (totalCount <= 0)
            return [];

        var resolutionProgressState = new PackDownloadProgressState();
        var downloadProgressState = new PackDownloadProgressState();
        var manualDownloadsByIndex = new ManualModpackDownload?[totalCount];
        progress?.Report(new LauncherProgress(ImportProgressStages.ResolvingPackFiles, $"0/{totalCount}", 0));
        progress?.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, string.Empty, 0));

        var fileTasks = preparedModpack.Files
            .Select((file, index) => ProcessPackFileAsync(
                preparedModpack,
                file,
                index,
                instance,
                curseForgeApiKey,
                downloadSpeedLimitMbPerSecond,
                totalCount,
                progress,
                cancellationToken,
                manualDownloadsByIndex,
                resolutionProgressState,
                downloadProgressState))
            .ToArray();

        await Task.WhenAll(fileTasks).ConfigureAwait(false);

        return manualDownloadsByIndex
            .Where(download => download is not null)
            .Cast<ManualModpackDownload>()
            .ToList();
    }

    public Task CopyOverridesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedModpack);
        ArgumentNullException.ThrowIfNull(instance);

        if (string.IsNullOrWhiteSpace(preparedModpack.OverridesDirectory)
            || !Directory.Exists(preparedModpack.OverridesDirectory))
        {
            return Task.CompletedTask;
        }

        progress?.Report(new LauncherProgress(ImportProgressStages.CopyingOverrides, string.Empty, 100));
        CopyOverrides(preparedModpack.OverridesDirectory, instance.InstanceDirectory);
        return Task.CompletedTask;
    }

    public Task<string?> WriteManualDownloadsFileAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IReadOnlyList<ManualModpackDownload> manualDownloads,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedModpack);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(manualDownloads);

        var filePath = manualDownloads.Count > 0
            ? WriteManualDownloadsFile(instance, preparedModpack, manualDownloads)
            : null;
        return Task.FromResult(filePath);
    }

    public async Task InstallContentAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var manualDownloads = await DownloadFilesAsync(
            preparedModpack,
            instance,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
        await CopyOverridesAsync(preparedModpack, instance, progress, cancellationToken).ConfigureAwait(false);
        preparedModpack.ManualDownloads = manualDownloads;
        preparedModpack.ManualDownloadsFilePath = await WriteManualDownloadsFileAsync(
            preparedModpack,
            instance,
            manualDownloads,
            cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        IProgress<LauncherProgress>? progress)
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
                cancellationToken,
                progress).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        IProgress<LauncherProgress>? progress)
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
            var downloads = ParseCurseForgeFiles(manifest.RootElement);
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

    private static IReadOnlyList<PreparedModpackDownload> ParseCurseForgeFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return [];

        var manifestFiles = new List<PreparedModpackDownload>(files.GetArrayLength());
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

            manifestFiles.Add(new PreparedModpackDownload
            {
                ProjectId = projectId,
                FileId = fileId,
                TargetDirectory = "mods"
            });
        }

        return manifestFiles;
    }

    private static void ReportCurseForgeResolutionProgress(
        IProgress<LauncherProgress>? progress,
        int completedCount,
        int totalCount)
    {
        if (progress is null || totalCount <= 0)
            return;

        progress.Report(new LauncherProgress(
            ImportProgressStages.ResolvingPackFiles,
            $"{completedCount}/{totalCount}",
            completedCount * 100d / totalCount));
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

    private async Task ProcessPackFileAsync(
        PreparedModpack preparedModpack,
        PreparedModpackDownload file,
        int fileIndex,
        GameInstance instance,
        string? curseForgeApiKey,
        int downloadSpeedLimitMbPerSecond,
        int totalCount,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        ManualModpackDownload?[] manualDownloadsByIndex,
        PackDownloadProgressState resolutionProgressState,
        PackDownloadProgressState downloadProgressState)
    {
        try
        {
            var resolution = await ResolvePackFileAsync(
                preparedModpack,
                file,
                totalCount,
                progress,
                resolutionProgressState,
                curseForgeApiKey,
                cancellationToken).ConfigureAwait(false);

            if (resolution.ManualDownload is not null)
            {
                manualDownloadsByIndex[fileIndex] = resolution.ManualDownload;
                return;
            }

            if (resolution.Download is null)
                throw new InvalidOperationException("Resolved modpack download was unexpectedly missing.");

            ReportPackDownloadProgress(progress, resolution.Download.FileName, downloadProgressState.ReadCompletedCount(), totalCount);
            manualDownloadsByIndex[fileIndex] = await DownloadResolvedPackFileAsync(
                preparedModpack,
                resolution.Download,
                instance,
                curseForgeApiKey,
                downloadSpeedLimitMbPerSecond,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var completedCount = downloadProgressState.IncrementCompletedCount();
            ReportPackDownloadProgress(progress, file.FileName, completedCount, totalCount);
        }
    }

    private async Task<PackFileResolution> ResolvePackFileAsync(
        PreparedModpack preparedModpack,
        PreparedModpackDownload file,
        int totalCount,
        IProgress<LauncherProgress>? progress,
        PackDownloadProgressState resolutionProgressState,
        string? curseForgeApiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            if (preparedModpack.PackageKind is not ModpackPackageKind.CurseForge)
            {
                return new PackFileResolution(
                    new ResolvedPackDownload(
                        string.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.RelativePath) : file.FileName,
                        string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
                        file.RelativePath,
                        file.SourceUrl,
                        [],
                        file.ProjectId,
                        file.FileId,
                        file.Sha1,
                        file.Sha512),
                    null);
            }

            if (string.IsNullOrWhiteSpace(curseForgeApiKey))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.MissingCurseForgeApiKey,
                    "CurseForge API key was not configured.");
            }

            var projectId = file.ProjectId
                ?? throw new ModpackImportException(ModpackImportFailureReason.InvalidManifest, "CurseForge project id is missing.");
            var fileId = file.FileId
                ?? throw new ModpackImportException(ModpackImportFailureReason.InvalidManifest, "CurseForge file id is missing.");

            var resolvedFile = await curseForgeApiClient
                .GetFileDownloadAsync(projectId, fileId, curseForgeApiKey, cancellationToken)
                .ConfigureAwait(false);
            var targetDirectory = string.IsNullOrWhiteSpace(file.TargetDirectory) ? "mods" : file.TargetDirectory;
            var relativePath = string.IsNullOrWhiteSpace(file.RelativePath)
                ? Path.Combine(targetDirectory, resolvedFile.FileName)
                : file.RelativePath;

            logger.LogInformation(
                "Resolved CurseForge modpack file. ProjectId={ProjectId} FileId={FileId} FileName={FileName} FallbackUrlCount={FallbackUrlCount}",
                projectId,
                fileId,
                resolvedFile.FileName,
                resolvedFile.FallbackUrls.Count);

            return new PackFileResolution(
                new ResolvedPackDownload(
                    resolvedFile.FileName,
                    resolvedFile.DisplayName,
                    relativePath,
                    resolvedFile.PrimaryUrl,
                    resolvedFile.FallbackUrls,
                    resolvedFile.ProjectId,
                    resolvedFile.FileId,
                    resolvedFile.Sha1,
                    resolvedFile.Sha512),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (preparedModpack.PackageKind is ModpackPackageKind.CurseForge)
        {
            logger.LogWarning(
                exception,
                "Failed to resolve CurseForge modpack file and will add it to the manual download list. ProjectId={ProjectId} FileId={FileId}",
                file.ProjectId,
                file.FileId);
            return new PackFileResolution(
                null,
                new ManualModpackDownload
                {
                    ProjectId = file.ProjectId,
                    FileId = file.FileId,
                    FileName = string.IsNullOrWhiteSpace(file.FileName) ? $"project-{file.ProjectId}-file-{file.FileId}" : file.FileName,
                    DisplayName = string.IsNullOrWhiteSpace(file.DisplayName) ? $"CurseForge {file.ProjectId}/{file.FileId}" : file.DisplayName,
                    SuggestedUrl = string.Empty,
                    FailureSummary = BuildManualDownloadFailureSummary(exception)
                });
        }
        finally
        {
            var completedCount = resolutionProgressState.IncrementCompletedCount();
            ReportCurseForgeResolutionProgress(progress, completedCount, totalCount);
        }
    }

    private async Task<ManualModpackDownload?> DownloadResolvedPackFileAsync(
        PreparedModpack preparedModpack,
        ResolvedPackDownload file,
        GameInstance instance,
        string? curseForgeApiKey,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var targetPath = ModpackArchiveUtility.GetValidatedTargetPath(instance.InstanceDirectory, file.RelativePath);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        var downloadDirectory = Path.Combine(preparedModpack.WorkingDirectory, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var tempFilePath = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N"));
        var sourceUrls = new List<string> { file.PrimaryUrl };
        foreach (var fallbackSourceUrl in file.FallbackSourceUrls)
        {
            if (!string.Equals(file.PrimaryUrl, fallbackSourceUrl, StringComparison.OrdinalIgnoreCase)
                && !sourceUrls.Contains(fallbackSourceUrl, StringComparer.OrdinalIgnoreCase))
            {
                sourceUrls.Add(fallbackSourceUrl);
            }
        }

        Exception? lastException = null;
        string? lastFailureSummary = null;

        try
        {
            foreach (var sourceUrl in sourceUrls)
            {
                if (!ModpackArchiveUtility.IsSupportedHttpUrl(sourceUrl))
                {
                    throw new ModpackImportException(
                        ModpackImportFailureReason.InvalidManifest,
                        $"Unsupported download URL: {sourceUrl}");
                }

                TryDeleteFile(tempFilePath);
                try
                {
                    await DownloadToTemporaryFileAsync(
                        sourceUrl,
                        tempFilePath,
                        curseForgeApiKey,
                        downloadSpeedLimitMbPerSecond,
                        cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(file.Sha512))
                        await VerifyHashAsync(tempFilePath, file.Sha512, HashAlgorithmName.SHA512, cancellationToken).ConfigureAwait(false);
                    else if (!string.IsNullOrWhiteSpace(file.Sha1))
                        await VerifyHashAsync(tempFilePath, file.Sha1, HashAlgorithmName.SHA1, cancellationToken).ConfigureAwait(false);

                    File.Move(tempFilePath, targetPath, overwrite: true);
                    logger.LogInformation(
                        "Downloaded modpack file. PackageKind={PackageKind} FileName={FileName} ProjectId={ProjectId} FileId={FileId} SourceUrl={SourceUrl} UsedFallback={UsedFallback}",
                        preparedModpack.PackageKind,
                        file.FileName,
                        file.ProjectId,
                        file.FileId,
                        sourceUrl,
                        !string.Equals(sourceUrl, file.PrimaryUrl, StringComparison.OrdinalIgnoreCase));
                    return null;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (preparedModpack.PackageKind is ModpackPackageKind.CurseForge)
                {
                    lastException = exception;
                    lastFailureSummary = BuildManualDownloadFailureSummary(exception);
                    logger.LogWarning(
                        exception,
                        "Failed to download CurseForge modpack file. ProjectId={ProjectId} FileId={FileId} FileName={FileName} SourceUrl={SourceUrl}",
                        file.ProjectId,
                        file.FileId,
                        file.FileName,
                        sourceUrl);
                }
            }

            if (preparedModpack.PackageKind is not ModpackPackageKind.CurseForge)
                throw lastException ?? new InvalidOperationException($"Failed to download modpack file: {file.FileName}");

            return new ManualModpackDownload
            {
                ProjectId = file.ProjectId,
                FileId = file.FileId,
                FileName = file.FileName,
                DisplayName = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
                SuggestedUrl = sourceUrls.FirstOrDefault() ?? string.Empty,
                FailureSummary = lastFailureSummary ?? "download_failed"
            };
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    private static void ReportPackDownloadProgress(
        IProgress<LauncherProgress>? progress,
        string fileName,
        int completedCount,
        int totalCount)
    {
        if (progress is null || totalCount <= 0)
            return;

        progress.Report(new LauncherProgress(
            ImportProgressStages.DownloadingPackFiles,
            fileName,
            completedCount * 100d / totalCount));
    }

    private sealed class PackDownloadProgressState
    {
        private int completedCount;

        public int ReadCompletedCount() => Volatile.Read(ref completedCount);

        public int IncrementCompletedCount() => Interlocked.Increment(ref completedCount);
    }

    private async Task DownloadToTemporaryFileAsync(
        string sourceUrl,
        string tempFilePath,
        string? curseForgeApiKey,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        if (!string.IsNullOrWhiteSpace(curseForgeApiKey)
            && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
            && CurseForgeDownloadHosts.Contains(sourceUri.Host))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", curseForgeApiKey);
        }

        await using var lease = await limiter.AcquireModpackDownloadSlotAsync(cancellationToken).ConfigureAwait(false);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyWithThrottleAsync(source, destination, bandwidthLimiter, cancellationToken).ConfigureAwait(false);
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

    private async Task VerifyHashAsync(
        string filePath,
        string expectedHash,
        HashAlgorithmName algorithmName,
        CancellationToken cancellationToken)
    {
        await using var lease = await limiter.AcquireHashSlotAsync(cancellationToken).ConfigureAwait(false);
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

    private sealed record PackFileResolution(ResolvedPackDownload? Download, ManualModpackDownload? ManualDownload);

    private sealed record ResolvedPackDownload(
        string FileName,
        string DisplayName,
        string RelativePath,
        string PrimaryUrl,
        IReadOnlyList<string> FallbackSourceUrls,
        long? ProjectId,
        long? FileId,
        string? Sha1,
        string? Sha512);

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

    private string WriteManualDownloadsFile(
        GameInstance instance,
        PreparedModpack preparedModpack,
        IReadOnlyList<ManualModpackDownload> manualDownloads)
    {
        var filePath = Path.Combine(instance.InstanceDirectory, ModpackManualDownloads.FileName);
        Directory.CreateDirectory(instance.InstanceDirectory);

        using var writer = new StreamWriter(filePath, append: false);
        writer.WriteLine($"instance={instance.Name}");
        writer.WriteLine($"package={preparedModpack.PackageName}");
        writer.WriteLine($"generatedAt={DateTimeOffset.Now:O}");
        writer.WriteLine();

        foreach (var manualDownload in manualDownloads)
        {
            writer.WriteLine($"fileName={manualDownload.FileName}");
            writer.WriteLine($"displayName={manualDownload.DisplayName}");
            writer.WriteLine($"projectId={manualDownload.ProjectId}");
            writer.WriteLine($"fileId={manualDownload.FileId}");
            writer.WriteLine($"suggestedUrl={manualDownload.SuggestedUrl}");
            writer.WriteLine($"failure={manualDownload.FailureSummary}");
            writer.WriteLine();
        }

        logger.LogInformation(
            "Wrote modpack manual downloads file. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} ManualDownloadCount={ManualDownloadCount} FilePath={FilePath}",
            instance.Id,
            instance.InstanceDirectory,
            manualDownloads.Count,
            filePath);
        return filePath;
    }

    private static string BuildManualDownloadFailureSummary(Exception exception)
    {
        if (exception is ModpackImportException modpackException
            && modpackException.FailureReason is ModpackImportFailureReason.HashMismatch)
        {
            return "hash_mismatch";
        }

        if (exception is HttpRequestException httpRequestException && httpRequestException.StatusCode is { } statusCode)
            return $"http_{(int)statusCode}";

        return exception.GetType().Name;
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
        if (TryGetString(dependencies, "neoforge", out var neoForgeVersion))
            loaderEntries.Add((LoaderKind.NeoForge, neoForgeVersion));

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
        if (id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
            return (LoaderKind.NeoForge, id["neoforge-".Length..]);
        if (id.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
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
