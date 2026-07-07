using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Launcher.Application;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Updates;

public sealed class LauncherSelfUpdateService : ILauncherSelfUpdateService
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);
    private const string UpdatesDirectoryName = "updates";
    private const string CacheDirectoryName = "cache";
    private const string LogDirectoryName = "log";
    private const string UserAgent = "MineDock-Launcher";

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

        if (!TryValidateDownloadUrl(update.DownloadUrl, update.DownloadFileName, out var downloadUri, out var fileName))
            return LauncherSelfUpdateStartResult.Failed("Update download URL is invalid.");

        try
        {
            var downloadPath = await DownloadUpdateExecutableAsync(
                    update.Version,
                    fileName,
                    downloadUri,
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
        string version,
        string fileName,
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(
            baseDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            CacheDirectoryName,
            UpdatesDirectoryName,
            SanitizePathSegment(version));
        Directory.CreateDirectory(updateDirectory);

        var downloadPath = Path.Combine(updateDirectory, fileName);
        var tempPath = downloadPath + ".download";
        logger?.LogInformation(
            "Downloading launcher update executable. Version={Version} Url={Url} TargetPath={TargetPath}",
            version,
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

        File.Move(tempPath, downloadPath, overwrite: true);
        logger?.LogInformation(
            "Launcher update executable downloaded. Version={Version} Path={Path} SizeBytes={SizeBytes}",
            version,
            downloadPath,
            fileInfo.Length);
        return downloadPath;
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
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}
