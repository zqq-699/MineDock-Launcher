/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class LocalModpackPackageService : IModpackPackageService
{
    private const int MaxPackFileProcessingConcurrency = 16;
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly IImportConcurrencyLimiter limiter;
    private readonly CurseForgeApiClient curseForgeApiClient;
    private readonly ICurseForgeApiKeyResolver curseForgeApiKeyResolver;
    private readonly ModpackFileDownloader fileDownloader;
    private readonly ILogger<LocalModpackPackageService> logger;

    public LocalModpackPackageService(
        LauncherPathProvider pathProvider,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        IImportConcurrencyLimiter? limiter = null,
        HttpClient? httpClient = null,
        CurseForgeApiClient? curseForgeApiClient = null,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null,
        ILogger<LocalModpackPackageService>? logger = null,
        ISettingsService? settingsService = null)
    {
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
        this.httpClient = httpClient ?? new HttpClient();
        this.curseForgeApiClient = curseForgeApiClient ?? new CurseForgeApiClient(this.httpClient, this.limiter);
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver
            ?? new CurseForgeApiKeyResolver(pathProvider, settingsService);
        fileDownloader = new ModpackFileDownloader(this.httpClient, downloadSpeedLimitState, this.limiter);
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

        logger.LogInformation(
            "Preparing local modpack archive. ArchivePath={ArchivePath}",
            normalizedArchivePath);

        return Path.GetExtension(normalizedArchivePath).ToLowerInvariant() switch
        {
            ".mrpack" => await PrepareModrinthAsync(normalizedArchivePath, cancellationToken).ConfigureAwait(false),
            ".zip" => await PrepareZipAsync(normalizedArchivePath, cancellationToken, progress).ConfigureAwait(false),
            _ => throw new ModpackImportException(
                ModpackImportFailureReason.UnsupportedArchive,
                $"Unsupported modpack archive type: {normalizedArchivePath}")
        };
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
            ? await GetCurseForgeApiKeyAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var totalCount = preparedModpack.Files.Count;
        if (totalCount <= 0)
            return [];

        var resolutionProgressState = new PackDownloadProgressState();
        var downloadProgressState = new PackDownloadProgressState();
        var manualDownloadsByIndex = new ManualModpackDownload?[totalCount];
        progress?.Report(new LauncherProgress(ImportProgressStages.ResolvingPackFiles, $"0/{totalCount}", 0));
        progress?.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, string.Empty, 0));

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxPackFileProcessingConcurrency,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var file = preparedModpack.Files[index];
                await ProcessPackFileAsync(
                    preparedModpack,
                    file,
                    index,
                    instance,
                    curseForgeApiKey,
                    downloadSpeedLimitMbPerSecond,
                    totalCount,
                    progress,
                    token,
                    manualDownloadsByIndex,
                    resolutionProgressState,
                    downloadProgressState).ConfigureAwait(false);
            }).ConfigureAwait(false);

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

        if (!preparedModpack.HasOverrides)
            return Task.CompletedTask;

        progress?.Report(new LauncherProgress(ImportProgressStages.CopyingOverrides, string.Empty, 100));
        return ModpackOverrideExtractor.CopyOverridesAsync(
            preparedModpack,
            instance.InstanceDirectory,
            cancellationToken);
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

        if (string.IsNullOrWhiteSpace(preparedModpack.WorkingDirectory))
            return Task.CompletedTask;

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
        if (CurseForgeModpackFormatReader.HasManifest(archive))
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
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return await PrepareModrinthArchiveAsync(
            archive,
            archivePath,
            embeddedModrinthEntryName: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedModpack> PrepareZipAsync(
        string archivePath,
        CancellationToken cancellationToken,
        IProgress<LauncherProgress>? progress)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (CurseForgeModpackFormatReader.HasManifest(archive))
        {
            return await PrepareCurseForgeArchiveAsync(
                archive,
                archivePath,
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
        CancellationToken cancellationToken)
    {
        await using var stream = await ModpackArchiveUtility.CopyZipEntryToMemoryAsync(
            mrpackEntry,
            ModpackArchiveUtility.MaxEmbeddedModpackBytes,
            cancellationToken).ConfigureAwait(false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return await PrepareModrinthArchiveAsync(
            archive,
            sourceArchivePath,
            mrpackEntry.FullName,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateEmbeddedModrinthAsync(
        ZipArchiveEntry mrpackEntry,
        CancellationToken cancellationToken)
    {
        await using var stream = await ModpackArchiveUtility.CopyZipEntryToMemoryAsync(
            mrpackEntry,
            ModpackArchiveUtility.MaxEmbeddedModpackBytes,
            cancellationToken).ConfigureAwait(false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        await ValidateModrinthArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateModrinthArchiveAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        await ModrinthModpackFormatReader.ValidateAsync(archive, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedModpack> PrepareModrinthArchiveAsync(
        ZipArchive archive,
        string sourceArchivePath,
        string? embeddedModrinthEntryName,
        CancellationToken cancellationToken)
    {
        return await ModrinthModpackFormatReader.ReadAsync(
            archive,
            sourceArchivePath,
            embeddedModrinthEntryName,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateCurseForgeArchiveAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        await CurseForgeModpackFormatReader.ValidateAsync(archive, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreparedModpack> PrepareCurseForgeArchiveAsync(
        ZipArchive archive,
        string sourceArchivePath,
        CancellationToken cancellationToken,
        IProgress<LauncherProgress>? progress)
    {
        return await CurseForgeModpackFormatReader.ReadAsync(
            archive,
            sourceArchivePath,
            cancellationToken).ConfigureAwait(false);
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

        var tempFilePath = Path.Combine(
            targetDirectory ?? instance.InstanceDirectory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.download");
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

                ModpackFileDownloader.DeleteFileIfExists(tempFilePath);
                try
                {
                    await fileDownloader.DownloadToTemporaryFileAsync(
                        sourceUrl,
                        tempFilePath,
                        curseForgeApiKey,
                        downloadSpeedLimitMbPerSecond,
                        cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(file.Sha512))
                        await fileDownloader.VerifyHashAsync(
                            tempFilePath,
                            file.Sha512,
                            HashAlgorithmName.SHA512,
                            cancellationToken).ConfigureAwait(false);
                    else if (!string.IsNullOrWhiteSpace(file.Sha1))
                        await fileDownloader.VerifyHashAsync(
                            tempFilePath,
                            file.Sha1,
                            HashAlgorithmName.SHA1,
                            cancellationToken).ConfigureAwait(false);

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
            ModpackFileDownloader.DeleteFileIfExists(tempFilePath);
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

    private async Task<string> GetCurseForgeApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKey = await curseForgeApiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

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
