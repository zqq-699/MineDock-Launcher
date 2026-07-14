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
using System.Net.Http;
using System.Security.Cryptography;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Modpacks;

/// <summary>
/// 并行解析整合包文件来源，校验下载内容，并把无法自动获取的 CurseForge 文件转为手动清单。
/// </summary>
internal sealed class ModpackFileResolutionService
{
    private const int MaxProcessingConcurrency = 16;
    private readonly CurseForgeApiClient curseForgeApiClient;
    private readonly ICurseForgeApiKeyResolver curseForgeApiKeyResolver;
    private readonly ModpackFileDownloader fileDownloader;
    private readonly ILogger logger;

    public ModpackFileResolutionService(
        CurseForgeApiClient curseForgeApiClient,
        ICurseForgeApiKeyResolver curseForgeApiKeyResolver,
        ModpackFileDownloader fileDownloader,
        ILogger logger)
    {
        this.curseForgeApiClient = curseForgeApiClient;
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver;
        this.fileDownloader = fileDownloader;
        this.logger = logger;
    }

    /// <summary>
    /// 限制并发地解析和下载整合包全部文件，并按原顺序返回需要手动处理的项目。
    /// </summary>
    public async Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var apiKey = preparedModpack.PackageKind is ModpackPackageKind.CurseForge
            ? await GetCurseForgeApiKeyAsync(cancellationToken).ConfigureAwait(false)
            : null;
        using var context = new DownloadBatchContext(
            preparedModpack,
            instance,
            apiKey,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            progress);
        if (context.TotalCount == 0)
            return [];

        context.ReportStarted();
        // 结果按原文件索引写入预分配槽位，限制并发的同时保持手动下载清单顺序稳定。
        await Parallel.ForEachAsync(
            Enumerable.Range(0, context.TotalCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxProcessingConcurrency,
                CancellationToken = cancellationToken
            },
            (index, token) => ResolvePackFileAtIndexAsync(context, index, token)).ConfigureAwait(false);
        ValidateUniqueDownloadTargets(context);
        await Parallel.ForEachAsync(
            Enumerable.Range(0, context.TotalCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxProcessingConcurrency,
                CancellationToken = cancellationToken
            },
            (index, token) => DownloadPackFileAtIndexAsync(context, index, token)).ConfigureAwait(false);
        return context.ManualDownloads
            .Where(download => download is not null)
            .Cast<ManualModpackDownload>()
            .ToArray();
    }

    /// <summary>
    /// 处理清单中的单个文件槽位，并确保无论结果如何都会推进总体进度。
    /// </summary>
    private async ValueTask ResolvePackFileAtIndexAsync(
        DownloadBatchContext context,
        int fileIndex,
        CancellationToken cancellationToken)
    {
        var file = context.Package.Files[fileIndex];
        var resolution = await ResolvePackFileAsync(context, file, cancellationToken).ConfigureAwait(false);
        context.Resolutions[fileIndex] = resolution;
        if (resolution.ManualDownload is not null)
            context.ManualDownloads[fileIndex] = resolution.ManualDownload;
    }

    private async ValueTask DownloadPackFileAtIndexAsync(
        DownloadBatchContext context,
        int fileIndex,
        CancellationToken cancellationToken)
    {
        var file = context.Package.Files[fileIndex];
        var resolution = context.Resolutions[fileIndex]
            ?? throw new InvalidOperationException("Resolved modpack file was unexpectedly missing.");
        try
        {
            if (resolution.ManualDownload is not null)
                return;
            if (resolution.Download is null)
                throw new InvalidOperationException("Resolved modpack download was unexpectedly missing.");
            context.ReportDownload(resolution.Download.FileName, completed: false);
            context.ManualDownloads[fileIndex] = await DownloadResolvedPackFileAsync(
                context,
                resolution.Download,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.ReportDownload(file.FileName, completed: true);
        }
    }

    private static void ValidateUniqueDownloadTargets(DownloadBatchContext context)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolution in context.Resolutions)
        {
            if (resolution?.Download is not { } download)
                continue;
            var targetPath = ModpackArchiveUtility.GetValidatedTargetPath(
                context.Instance.InstanceDirectory,
                download.RelativePath);
            if (!targets.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath))))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.InvalidManifest,
                    $"Multiple modpack files resolve to the same target path: {download.RelativePath}");
            }
        }
    }

    /// <summary>
    /// 将清单文件转换为可下载描述；CurseForge 无法解析时生成手动下载项。
    /// </summary>
    private async Task<PackFileResolution> ResolvePackFileAsync(
        DownloadBatchContext context,
        PreparedModpackDownload file,
        CancellationToken cancellationToken)
    {
        try
        {
            if (context.Package.PackageKind is not ModpackPackageKind.CurseForge)
                return new PackFileResolution(CreateDirectDownload(file), null);
            if (string.IsNullOrWhiteSpace(context.CurseForgeApiKey))
            {
                throw new ModpackImportException(
                    ModpackImportFailureReason.MissingCurseForgeApiKey,
                    "CurseForge API key was not configured.");
            }
            var projectId = file.ProjectId
                ?? throw new ModpackImportException(ModpackImportFailureReason.InvalidManifest, "CurseForge project id is missing.");
            var fileId = file.FileId
                ?? throw new ModpackImportException(ModpackImportFailureReason.InvalidManifest, "CurseForge file id is missing.");
            var resolved = await curseForgeApiClient
                .GetFileDownloadAsync(projectId, fileId, context.CurseForgeApiKey, cancellationToken)
                .ConfigureAwait(false);
            var targetDirectory = string.IsNullOrWhiteSpace(file.TargetDirectory) ? "mods" : file.TargetDirectory;
            var relativePath = string.IsNullOrWhiteSpace(file.RelativePath)
                ? Path.Combine(targetDirectory, resolved.FileName)
                : file.RelativePath;
            logger.LogInformation(
                "Resolved CurseForge modpack file. ProjectId={ProjectId} FileId={FileId} FileName={FileName} FallbackUrlCount={FallbackUrlCount}",
                projectId,
                fileId,
                resolved.FileName,
                resolved.FallbackUrls.Count);
            return new PackFileResolution(
                new ResolvedPackDownload(
                    resolved.FileName,
                    resolved.DisplayName,
                    relativePath,
                    resolved.PrimaryUrl,
                    resolved.FallbackUrls,
                    resolved.ProjectId,
                    resolved.FileId,
                    resolved.Sha1,
                    resolved.Sha512),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (context.Package.PackageKind is ModpackPackageKind.CurseForge)
        {
            // CurseForge 可能因授权限制不给下载地址，此类失败降级为手动下载而非终止整个导入。
            logger.LogWarning(
                exception,
                "Failed to resolve CurseForge modpack file and will add it to the manual download list. ProjectId={ProjectId} FileId={FileId}",
                file.ProjectId,
                file.FileId);
            return new PackFileResolution(null, CreateManualDownload(file, exception));
        }
        finally
        {
            context.ReportResolutionCompleted();
        }
    }

    /// <summary>
    /// 依次尝试主地址和回退地址，验证临时文件后提交，全部失败时按包类型决定降级或抛出。
    /// </summary>
    private async Task<ManualModpackDownload?> DownloadResolvedPackFileAsync(
        DownloadBatchContext context,
        ResolvedPackDownload file,
        CancellationToken cancellationToken)
    {
        var targetPath = ModpackArchiveUtility.GetValidatedTargetPath(
            context.Instance.InstanceDirectory,
            file.RelativePath);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);
        // 每个候选 URL 都写入临时文件并完成哈希校验，成功后才覆盖实例中的目标文件。
        var tempPath = Path.Combine(
            targetDirectory ?? context.Instance.InstanceDirectory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.download");
        var sourceUrls = BuildSourceUrls(file);
        Exception? lastException = null;
        string? lastFailureSummary = null;
        try
        {
            foreach (var sourceUrl in sourceUrls)
            {
                ValidateSourceUrl(sourceUrl);
                ModpackFileDownloader.DeleteFileIfExists(tempPath);
                try
                {
                    await DownloadAndVerifyAsync(context, file, sourceUrl, tempPath, cancellationToken)
                        .ConfigureAwait(false);
                    File.Move(tempPath, targetPath, overwrite: true);
                    LogDownloaded(context.Package.PackageKind, file, sourceUrl);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (context.Package.PackageKind is ModpackPackageKind.CurseForge)
                {
                    lastException = exception;
                    lastFailureSummary = BuildFailureSummary(exception);
                    logger.LogWarning(
                        exception,
                        "Failed to download CurseForge modpack file. ProjectId={ProjectId} FileId={FileId} FileName={FileName} SourceUrl={SourceUrl}",
                        file.ProjectId,
                        file.FileId,
                        file.FileName,
                        sourceUrl);
                }
            }
            if (context.Package.PackageKind is not ModpackPackageKind.CurseForge)
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
            ModpackFileDownloader.DeleteFileIfExists(tempPath);
        }
    }

    /// <summary>
    /// 下载到临时文件并优先使用 SHA-512、其次 SHA-1 验证内容完整性。
    /// </summary>
    private async Task DownloadAndVerifyAsync(
        DownloadBatchContext context,
        ResolvedPackDownload file,
        string sourceUrl,
        string tempPath,
        CancellationToken cancellationToken)
    {
        await fileDownloader.DownloadToTemporaryFileAsync(
            sourceUrl,
            tempPath,
            context.CurseForgeApiKey,
            context.DownloadSourcePreference,
            file.Sha1,
            file.Sha512,
            context.DownloadSpeedLimitMbPerSecond,
            context.SpeedReporter,
            cancellationToken).ConfigureAwait(false);
    }

    private void LogDownloaded(ModpackPackageKind packageKind, ResolvedPackDownload file, string sourceUrl)
    {
        logger.LogInformation(
            "Downloaded modpack file. PackageKind={PackageKind} FileName={FileName} ProjectId={ProjectId} FileId={FileId} SourceUrl={SourceUrl} UsedFallback={UsedFallback}",
            packageKind,
            file.FileName,
            file.ProjectId,
            file.FileId,
            sourceUrl,
            !string.Equals(sourceUrl, file.PrimaryUrl, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> GetCurseForgeApiKeyAsync(CancellationToken cancellationToken)
    {
        var key = await curseForgeApiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(key))
            return key;
        throw new ModpackImportException(
            ModpackImportFailureReason.MissingCurseForgeApiKey,
            "CurseForge API key was not configured.");
    }

    private static ResolvedPackDownload CreateDirectDownload(PreparedModpackDownload file) => new(
        string.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.RelativePath) : file.FileName,
        string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
        file.RelativePath,
        file.SourceUrl,
        [],
        file.ProjectId,
        file.FileId,
        file.Sha1,
        file.Sha512);

    private static ManualModpackDownload CreateManualDownload(PreparedModpackDownload file, Exception exception) => new()
    {
        ProjectId = file.ProjectId,
        FileId = file.FileId,
        FileName = string.IsNullOrWhiteSpace(file.FileName) ? $"project-{file.ProjectId}-file-{file.FileId}" : file.FileName,
        DisplayName = string.IsNullOrWhiteSpace(file.DisplayName) ? $"CurseForge {file.ProjectId}/{file.FileId}" : file.DisplayName,
        SuggestedUrl = string.Empty,
        FailureSummary = BuildFailureSummary(exception)
    };

    private static List<string> BuildSourceUrls(ResolvedPackDownload file)
    {
        var urls = new List<string> { file.PrimaryUrl };
        foreach (var fallback in file.FallbackSourceUrls)
        {
            if (!string.Equals(file.PrimaryUrl, fallback, StringComparison.OrdinalIgnoreCase)
                && !urls.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(fallback);
            }
        }
        return urls;
    }

    private static void ValidateSourceUrl(string sourceUrl)
    {
        if (!ModpackArchiveUtility.IsSupportedHttpUrl(sourceUrl))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"Unsupported download URL: {sourceUrl}");
        }
    }

    private static string BuildFailureSummary(Exception exception)
    {
        if (exception is ModpackImportException { FailureReason: ModpackImportFailureReason.HashMismatch })
            return "hash_mismatch";
        if (exception is HttpRequestException { StatusCode: { } statusCode })
            return $"http_{(int)statusCode}";
        return exception.GetType().Name;
    }

    private sealed class DownloadBatchContext : IDisposable
    {
        private int resolvedCount;
        private int downloadedCount;
        private string? activeDownloadFileName;
        private readonly IProgress<LauncherProgress>? progress;

        public DownloadBatchContext(
            PreparedModpack package,
            GameInstance instance,
            string? curseForgeApiKey,
            DownloadSourcePreference downloadSourcePreference,
            int downloadSpeedLimitMbPerSecond,
            IProgress<LauncherProgress>? progress)
        {
            Package = package;
            Instance = instance;
            CurseForgeApiKey = curseForgeApiKey;
            DownloadSourcePreference = downloadSourcePreference;
            DownloadSpeedLimitMbPerSecond = downloadSpeedLimitMbPerSecond;
            this.progress = progress;
            SpeedReporter = new SlidingWindowDownloadSpeedReporter(
                progress,
                speedStage: ImportProgressStages.DownloadingPackFiles,
                inactiveStage: ImportProgressStages.DownloadingPackFiles,
                messageProvider: () => Volatile.Read(ref activeDownloadFileName) ?? string.Empty);
            Resolutions = new PackFileResolution?[package.Files.Count];
            ManualDownloads = new ManualModpackDownload?[package.Files.Count];
        }

        public PreparedModpack Package { get; }
        public GameInstance Instance { get; }
        public string? CurseForgeApiKey { get; }
        public DownloadSourcePreference DownloadSourcePreference { get; }
        public int DownloadSpeedLimitMbPerSecond { get; }
        public int TotalCount => Package.Files.Count;
        public PackFileResolution?[] Resolutions { get; }
        public ManualModpackDownload?[] ManualDownloads { get; }
        public SlidingWindowDownloadSpeedReporter SpeedReporter { get; }

        public void ReportStarted()
        {
            progress?.Report(new LauncherProgress(ImportProgressStages.ResolvingPackFiles, $"0/{TotalCount}", 0));
            progress?.Report(new LauncherProgress(ImportProgressStages.DownloadingPackFiles, string.Empty, 0));
        }

        public void ReportResolutionCompleted()
        {
            var count = Interlocked.Increment(ref resolvedCount);
            progress?.Report(new LauncherProgress(
                ImportProgressStages.ResolvingPackFiles,
                $"{count}/{TotalCount}",
                count * 100d / TotalCount));
        }

        public void ReportDownload(string fileName, bool completed)
        {
            if (!completed)
                Volatile.Write(ref activeDownloadFileName, fileName);
            var count = completed
                ? Interlocked.Increment(ref downloadedCount)
                : Volatile.Read(ref downloadedCount);
            progress?.Report(new LauncherProgress(
                ImportProgressStages.DownloadingPackFiles,
                fileName,
                count * 100d / TotalCount));
        }

        public void Dispose() => SpeedReporter.Dispose();
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
}
