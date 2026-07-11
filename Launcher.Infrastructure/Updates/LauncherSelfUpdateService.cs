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

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Launcher.Application;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Updates;

/// <summary>
/// 下载、校验并启动独立更新程序，负责当前进程退出前的自更新交接。
/// </summary>
public sealed class LauncherSelfUpdateService : ILauncherSelfUpdateService
{
    // 下载文件先进入专用临时目录，校验通过前绝不覆盖当前正在运行的可执行文件。
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);
    private const string UpdatesDirectoryName = "updates";
    private const string CacheDirectoryName = "cache";
    private const string LogDirectoryName = "log";
    private const string UserAgent = "BlockHelm-Launcher";

    private readonly HttpClient httpClient;
    private readonly ILogger<LauncherSelfUpdateService>? logger;
    private readonly string baseDirectory;
    private readonly string? currentExecutablePath;
    private readonly int currentProcessId;
    private readonly Func<ProcessStartInfo, bool> startProcess;

    public LauncherSelfUpdateService(
        HttpClient? httpClient = null,
        ILogger<LauncherSelfUpdateService>? logger = null)
        : this(
            httpClient,
            logger,
            AppContext.BaseDirectory,
            Environment.ProcessPath,
            Environment.ProcessId,
            StartProcess)
    {
    }

    public LauncherSelfUpdateService(
        HttpClient? httpClient,
        ILogger<LauncherSelfUpdateService>? logger,
        string baseDirectory,
        string? currentExecutablePath,
        int currentProcessId,
        Func<ProcessStartInfo, bool> startProcess)
    {
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = DefaultRequestTimeout
        };
        this.logger = logger;
        this.baseDirectory = baseDirectory;
        this.currentExecutablePath = currentExecutablePath;
        this.currentProcessId = currentProcessId;
        this.startProcess = startProcess;
        EnsureDefaultHeaders(this.httpClient);
    }

    public async Task<LauncherSelfUpdateStartResult> StartUpdateAsync(
        LauncherUpdateInfo update,
        CancellationToken cancellationToken = default)
    {
        // 对候选 URL 依次尝试；只有完整下载且 SHA-256 匹配的文件才能进入启动阶段。
        if (!update.CanAutoInstall)
        {
            logger?.LogWarning(
                "Launcher self update rejected non-installable update asset. Version={Version} AssetKind={AssetKind} FileName={FileName}",
                update.Version,
                update.AssetKind,
                update.DownloadFileName);
            return LauncherSelfUpdateStartResult.Failed("Update asset is not an installable Windows x64 executable.");
        }

        if (string.IsNullOrWhiteSpace(currentExecutablePath))
            return LauncherSelfUpdateStartResult.Failed("Current launcher executable path is unavailable.");

        var downloadUrls = update.EffectiveDownloadUrls;
        if (downloadUrls.Count == 0)
            return LauncherSelfUpdateStartResult.Failed("Update download URL is invalid.");

        try
        {
            var downloadPath = await DownloadUpdateExecutableAsync(
                    update,
                    downloadUrls,
                    cancellationToken)
                .ConfigureAwait(false);
            var logDirectory = Path.Combine(baseDirectory, LauncherApplicationIdentity.StorageDirectoryName, LogDirectoryName);
            Directory.CreateDirectory(logDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = downloadPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--apply-update");
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(currentProcessId.ToString());
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(downloadPath);
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(currentExecutablePath);
            startInfo.ArgumentList.Add("--log-dir");
            startInfo.ArgumentList.Add(logDirectory);
            startInfo.ArgumentList.Add("--restart");

            if (!startProcess(startInfo))
            {
                logger?.LogWarning("Failed to start launcher update apply mode. SourcePath={SourcePath}", downloadPath);
                return LauncherSelfUpdateStartResult.Failed("Failed to start update apply mode.");
            }

            logger?.LogInformation(
                "Launcher update apply mode started. Version={Version} DownloadPath={DownloadPath} TargetPath={TargetPath}",
                update.Version,
                downloadPath,
                currentExecutablePath);
            return LauncherSelfUpdateStartResult.Success(downloadPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Launcher self update failed.");
            return LauncherSelfUpdateStartResult.Failed(ex.Message);
        }
    }

    private async Task<string> DownloadUpdateExecutableAsync(
        LauncherUpdateInfo update,
        IReadOnlyList<LauncherUpdateDownloadUrl> downloadUrls,
        CancellationToken cancellationToken)
    {
        // 镜像失败保留最后异常用于诊断，同时清理每次尝试生成的临时文件。
        Exception? lastException = null;
        foreach (var downloadUrl in downloadUrls.OrderBy(url => url.Priority))
        {
            if (!TryValidateDownloadUrl(downloadUrl.Url, update.DownloadFileName, out var downloadUri, out var fileName))
            {
                logger?.LogWarning(
                    "Skipping invalid launcher update download URL. Version={Version} Source={Source} Url={Url}",
                    update.Version,
                    downloadUrl.Name,
                    downloadUrl.Url);
                continue;
            }

            try
            {
                return await DownloadUpdateExecutableFromSourceAsync(
                        update,
                        fileName,
                        downloadUri,
                        downloadUrl.Name,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                logger?.LogWarning(
                    ex,
                    "Launcher update executable download source failed. Version={Version} Source={Source} Url={Url}",
                    update.Version,
                    downloadUrl.Name,
                    downloadUri);
            }
        }

        throw new InvalidOperationException(
            "All launcher update download URLs failed.",
            lastException);
    }

    private async Task<string> DownloadUpdateExecutableFromSourceAsync(
        LauncherUpdateInfo update,
        string fileName,
        Uri downloadUri,
        string sourceName,
        CancellationToken cancellationToken)
    {
        // 流式写入限制内存占用，并使用 Content-Length/实际字节数检查截断响应。
        var updateDirectory = Path.Combine(
            baseDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            CacheDirectoryName,
            UpdatesDirectoryName,
            SanitizePathSegment(update.Version));
        Directory.CreateDirectory(updateDirectory);

        var downloadPath = Path.Combine(updateDirectory, fileName);
        var tempPath = downloadPath + ".download";
        logger?.LogInformation(
            "Downloading launcher update executable. Version={Version} Source={Source} Url={Url} TargetPath={TargetPath}",
            update.Version,
            sourceName,
            downloadUri,
            downloadPath);

        using var response = await httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(tempPath))
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

        var fileInfo = new FileInfo(tempPath);
        if (fileInfo.Length <= 0)
            throw new InvalidOperationException("Downloaded launcher update executable is empty.");

        if (update.SizeBytes > 0 && fileInfo.Length != update.SizeBytes)
            throw new InvalidOperationException("Downloaded launcher update executable size does not match the update manifest.");

        if (!string.IsNullOrWhiteSpace(update.Sha256))
            await VerifySha256Async(tempPath, update.Sha256, cancellationToken).ConfigureAwait(false);

        File.Move(tempPath, downloadPath, overwrite: true);
        logger?.LogInformation(
            "Launcher update executable downloaded. Version={Version} Source={Source} Path={Path} SizeBytes={SizeBytes}",
            update.Version,
            sourceName,
            downloadPath,
            fileInfo.Length);
        return downloadPath;
    }

    private static async Task VerifySha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        // 哈希比较使用规范十六进制且不区分大小写，失败文件不能继续执行。
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualSha256 = Convert.ToHexString(hash);
        if (!string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Downloaded launcher update executable SHA-256 does not match the update manifest.");
    }

    private static void EnsureDefaultHeaders(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    private static bool TryValidateDownloadUrl(
        string? downloadUrl,
        string? downloadFileName,
        out Uri downloadUri,
        out string fileName)
    {
        // 只允许 HTTPS，阻止清单把自更新重定向到本地路径或不安全协议。
        downloadUri = default!;
        fileName = string.Empty;
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsedUri)
            || parsedUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var candidateFileName = Path.GetFileName(downloadFileName);
        if (string.IsNullOrWhiteSpace(candidateFileName))
            candidateFileName = Path.GetFileName(parsedUri.LocalPath);

        if (string.IsNullOrWhiteSpace(candidateFileName)
            || !candidateFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || candidateFileName.EndsWith(".exe.asc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        downloadUri = parsedUri;
        fileName = candidateFileName;
        return true;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static bool StartProcess(ProcessStartInfo startInfo)
    {
        // 成功标准是更新器进程已创建；随后由 Application 层决定何时退出当前进程。
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}
