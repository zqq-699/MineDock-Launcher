/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Multiplayer;

internal sealed class EasyTierProvisioningService : IEasyTierProvisioningService
{
    internal const string EasyTierVersion = "2.6.4";
    private const long MaximumArchiveBytes = 40L * 1024 * 1024;
    private const long MaximumExtractedFileBytes = 64L * 1024 * 1024;
    private const string ManifestFileName = ".blockhelm-module.json";
    private static readonly string[] RequiredFileNames =
    [
        "easytier-core.exe",
        "easytier-cli.exe",
        "Packet.dll"
    ];

    private readonly MinecraftDownloadTransport transport;
    private readonly string moduleRoot;
    private readonly EasyTierDistribution distribution;
    private readonly ILogger<EasyTierProvisioningService> logger;
    private readonly SemaphoreSlim provisioningLock = new(1, 1);

    public EasyTierProvisioningService(
        LauncherPathProvider pathProvider,
        ILogger<EasyTierProvisioningService>? logger = null)
        : this(
            MinecraftHttpClientFactory.CreateTransportClient(),
            Path.Combine(pathProvider.DefaultDataDirectory, "tools", "easytier"),
            ResolveDistribution(RuntimeInformation.OSArchitecture),
            logger)
    {
    }

    internal EasyTierProvisioningService(
        HttpClient httpClient,
        string moduleRoot,
        EasyTierDistribution distribution,
        ILogger<EasyTierProvisioningService>? logger = null)
    {
        this.moduleRoot = Path.GetFullPath(moduleRoot);
        this.distribution = distribution;
        this.logger = logger ?? NullLogger<EasyTierProvisioningService>.Instance;
        transport = new MinecraftDownloadTransport(
            httpClient,
            new DownloadRetryOptions
            {
                MaxRedirects = 10,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(20)
            });
    }

    public EasyTierModule? TryGetAvailable()
    {
        try
        {
            var directory = GetInstallationDirectory();
            MinecraftPathGuard.EnsureWithin(directory, moduleRoot, "EasyTier installation directory");
            MinecraftPathGuard.EnsureNoReparsePoints(moduleRoot, directory, "EasyTier installation directory");
            if (!Directory.Exists(directory))
                return null;

            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
                return null;

            var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(manifestPath));
            if (manifest is null
                || !string.Equals(manifest.Version, EasyTierVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.Architecture, distribution.Architecture, StringComparison.Ordinal)
                || !string.Equals(manifest.ArchiveSha256, distribution.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (var fileName in RequiredFileNames)
            {
                var path = Path.Combine(directory, fileName);
                MinecraftPathGuard.EnsureSafeFileDestination(path, directory, $"EasyTier {fileName}");
                if (!manifest.FileSizes.TryGetValue(fileName, out var expectedLength)
                    || expectedLength <= 0
                    || !File.Exists(path)
                    || new FileInfo(path).Length != expectedLength)
                {
                    return null;
                }
            }

            return CreateModule(directory);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException)
        {
            logger.LogDebug(exception, "Ignoring an invalid EasyTier module installation.");
            return null;
        }
    }

    public async Task<EasyTierModule> EnsureAvailableAsync(
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var available = TryGetAvailable();
        if (available is not null)
            return available;

        await provisioningLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            available = TryGetAvailable();
            if (available is not null)
                return available;

            progress?.Report(new LauncherProgress("easytier-download", "Downloading EasyTier module", 0));
            return await DownloadAndInstallAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            provisioningLock.Release();
        }
    }

    private async Task<EasyTierModule> DownloadAndInstallAsync(
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var installationDirectory = GetInstallationDirectory();
        var archivePath = Path.Combine(moduleRoot, $".download-{Guid.NewGuid():N}.zip");
        var stagingDirectory = Path.Combine(moduleRoot, $".install-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(moduleRoot, $".backup-{Guid.NewGuid():N}");

        MinecraftPathGuard.EnsureSafeDirectory(moduleRoot, moduleRoot, "EasyTier module root");
        try
        {
            await DownloadVerifiedArchiveAsync(archivePath, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new LauncherProgress("easytier-extract", "Extracting EasyTier module", 92));
            await ExtractRequiredFilesAsync(archivePath, stagingDirectory, cancellationToken).ConfigureAwait(false);
            await WriteManifestAsync(stagingDirectory, cancellationToken).ConfigureAwait(false);

            PublishInstallation(stagingDirectory, installationDirectory, backupDirectory);
            var module = TryGetAvailable()
                ?? throw new InvalidDataException("The installed EasyTier module did not pass validation.");
            progress?.Report(new LauncherProgress("easytier-ready", "EasyTier module is ready", 100));
            logger.LogInformation(
                "EasyTier module prepared. Version={Version} Architecture={Architecture}",
                EasyTierVersion,
                distribution.Architecture);
            return module;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingDirectory);
            TryDeleteDirectory(backupDirectory);
        }
    }

    private async Task DownloadVerifiedArchiveAsync(
        string archivePath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        foreach (var downloadUri in distribution.DownloadUris)
        {
            try
            {
                TryDeleteFile(archivePath);
                progress?.Report(new LauncherProgress("easytier-download", "Downloading EasyTier module", 0));
                await DownloadArchiveAsync(downloadUri, archivePath, progress, cancellationToken).ConfigureAwait(false);
                await VerifyArchiveAsync(archivePath, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "EasyTier archive downloaded and verified. SourceHost={SourceHost}",
                    downloadUri.Host);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableSourceFailure(exception))
            {
                lastFailure = exception;
                logger.LogWarning(
                    exception,
                    "EasyTier download source failed; trying the next source. SourceHost={SourceHost}",
                    downloadUri.Host);
            }
        }

        TryDeleteFile(archivePath);
        if (lastFailure is not null)
            ExceptionDispatchInfo.Capture(lastFailure).Throw();
        throw new InvalidOperationException("No EasyTier download source is configured.");
    }

    private async Task DownloadArchiveAsync(
        Uri downloadUri,
        string archivePath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        MinecraftPathGuard.EnsureSafeFileDestination(archivePath, moduleRoot, "EasyTier temporary archive");
        await using var transportResult = await transport.SendAsync(
            downloadUri.AbsoluteUri,
            cancellationToken).ConfigureAwait(false);
        using var response = transportResult.Response;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumArchiveBytes)
            throw new InvalidDataException("The EasyTier archive is too large.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
            if (total > MaximumArchiveBytes)
                throw new InvalidDataException("The EasyTier archive is too large.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            var expected = response.Content.Headers.ContentLength;
            if (expected is > 0)
            {
                var percent = Math.Clamp(total * 90d / expected.Value, 0, 90);
                progress?.Report(new LauncherProgress("easytier-download", "Downloading EasyTier module", percent));
            }
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(hash, distribution.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The EasyTier archive SHA-256 checksum does not match.");
    }

    private static bool IsRecoverableSourceFailure(Exception exception) => exception is
        HttpRequestException
        or DownloadAttemptException
        or IOException
        or InvalidDataException
        or OperationCanceledException;

    private async Task ExtractRequiredFilesAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        MinecraftPathGuard.EnsureSafeDirectory(stagingDirectory, moduleRoot, "EasyTier staging directory");
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var fileName in RequiredFileNames)
        {
            var matches = archive.Entries
                .Where(entry => string.Equals(GetArchiveFileName(entry.FullName), fileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1 || matches[0].Length <= 0 || matches[0].Length > MaximumExtractedFileBytes)
                throw new InvalidDataException($"The EasyTier archive does not contain a valid {fileName} entry.");

            var destination = Path.Combine(stagingDirectory, fileName);
            MinecraftPathGuard.EnsureSafeFileDestination(destination, stagingDirectory, $"EasyTier {fileName}");
            await using var source = matches[0].Open();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyWithLimitAsync(source, target, MaximumExtractedFileBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteManifestAsync(string stagingDirectory, CancellationToken cancellationToken)
    {
        var sizes = RequiredFileNames.ToDictionary(
            fileName => fileName,
            fileName => new FileInfo(Path.Combine(stagingDirectory, fileName)).Length,
            StringComparer.OrdinalIgnoreCase);
        var manifest = new ModuleManifest(
            EasyTierVersion,
            distribution.Architecture,
            distribution.ArchiveSha256,
            sizes);
        var manifestPath = Path.Combine(stagingDirectory, ManifestFileName);
        MinecraftPathGuard.EnsureSafeFileDestination(manifestPath, stagingDirectory, "EasyTier module manifest");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest),
            cancellationToken).ConfigureAwait(false);
    }

    private void PublishInstallation(string stagingDirectory, string installationDirectory, string backupDirectory)
    {
        MinecraftPathGuard.EnsureWithin(installationDirectory, moduleRoot, "EasyTier installation directory");
        MinecraftPathGuard.EnsureNoReparsePoints(moduleRoot, installationDirectory, "EasyTier installation directory");
        Directory.CreateDirectory(Path.GetDirectoryName(installationDirectory)!);

        var movedExisting = false;
        try
        {
            if (Directory.Exists(installationDirectory))
            {
                Directory.Move(installationDirectory, backupDirectory);
                movedExisting = true;
            }

            Directory.Move(stagingDirectory, installationDirectory);
        }
        catch
        {
            if (movedExisting && !Directory.Exists(installationDirectory) && Directory.Exists(backupDirectory))
                Directory.Move(backupDirectory, installationDirectory);
            throw;
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream target,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException("An EasyTier archive entry is too large.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private EasyTierModule CreateModule(string directory) => new(
        EasyTierVersion,
        directory,
        Path.Combine(directory, "easytier-core.exe"),
        Path.Combine(directory, "easytier-cli.exe"),
        Path.Combine(directory, "Packet.dll"));

    private string GetInstallationDirectory() =>
        Path.Combine(moduleRoot, EasyTierVersion, $"easytier-windows-{distribution.Architecture}");

    private static string GetArchiveFileName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            MinecraftPathGuard.EnsureSafeFileDestination(path, moduleRoot, "EasyTier temporary file");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogDebug(exception, "Unable to remove an EasyTier temporary file.");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            MinecraftPathGuard.EnsureWithin(path, moduleRoot, "EasyTier temporary directory");
            MinecraftPathGuard.EnsureNoReparsePoints(moduleRoot, path, "EasyTier temporary directory");
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogDebug(exception, "Unable to remove an EasyTier temporary directory.");
        }
    }

    internal static EasyTierDistribution ResolveDistribution(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => new EasyTierDistribution(
            "arm64",
            [
                new Uri("https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-arm64-v2.6.4.zip")
            ],
            "37023f8a3451c9234b17ee2089a03dc344ce90d803b5b359cb6c46682b0549b4"),
        Architecture.X64 => new EasyTierDistribution(
            "x86_64",
            [
                new Uri("https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-x86_64-v2.6.4.zip")
            ],
            "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84"),
        _ => throw new PlatformNotSupportedException("EasyTier is supported only on Windows x64 and Arm64.")
    };

    internal sealed record EasyTierDistribution(
        string Architecture,
        IReadOnlyList<Uri> DownloadUris,
        string ArchiveSha256);

    private sealed record ModuleManifest(
        string Version,
        string Architecture,
        string ArchiveSha256,
        Dictionary<string, long> FileSizes);
}
