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
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

public sealed class ResourceProjectInstallationService : IResourceProjectInstallationService
{
    private const string WorkspacePrefix = "launcher-modpack-install-";
    private const string MarkerFileName = ".launcher-resource-install.json";
    private const string ActiveLockFileName = ".launcher-resource-install.lock";
    private static readonly JsonSerializerOptions MarkerJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IResourceCatalogService resourceCatalogService;
    private readonly ILocalModpackImportService localModpackImportService;
    private readonly ILogger<ResourceProjectInstallationService> logger;

    public ResourceProjectInstallationService(
        IResourceCatalogService resourceCatalogService,
        ILocalModpackImportService localModpackImportService,
        ILogger<ResourceProjectInstallationService> logger)
    {
        this.resourceCatalogService = resourceCatalogService;
        this.localModpackImportService = localModpackImportService;
        this.logger = logger;
    }

    public async Task<ResourceProjectInstallationPreparationResult> PrepareAsync(
        ResourceProjectInstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetExists = request.TargetKind switch
        {
            ResourceProjectInstallationTargetKind.LocalDirectory =>
                await resourceCatalogService.ProjectVersionDownloadExistsAsync(
                    request.Version,
                    RequireTargetDirectory(request),
                    cancellationToken).ConfigureAwait(false),
            ResourceProjectInstallationTargetKind.ExistingInstance =>
                await resourceCatalogService.ProjectVersionInstallExistsAsync(
                    request.Version,
                    RequireInstance(request),
                    cancellationToken).ConfigureAwait(false),
            ResourceProjectInstallationTargetKind.NewModpackInstance => false,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        return new ResourceProjectInstallationPreparationResult(targetExists);
    }

    public Task CleanupStaleWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CleanupStaleWorkspaces(cancellationToken), cancellationToken);
    }

    public async Task<ResourceProjectInstallationResult> ExecuteAsync(
        ResourceProjectInstallationRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        switch (request.TargetKind)
        {
            case ResourceProjectInstallationTargetKind.LocalDirectory:
            {
                var path = await DownloadProjectVersionAsync(
                    request.Version,
                    RequireTargetDirectory(request),
                    progress,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty, 99));
                return new ResourceProjectInstallationResult(InstalledPath: path);
            }
            case ResourceProjectInstallationTargetKind.ExistingInstance:
            {
                var path = await InstallProjectVersionAsync(
                    request.Version,
                    RequireInstance(request),
                    progress,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty, 99));
                return new ResourceProjectInstallationResult(InstalledPath: path);
            }
            case ResourceProjectInstallationTargetKind.NewModpackInstance:
                return await ImportModpackAsNewInstanceAsync(request.Version, progress, cancellationToken)
                    .ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private Task<string> DownloadProjectVersionAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken) =>
        resourceCatalogService is IResourceCatalogProgressReporter progressReporter
            ? progressReporter.DownloadProjectVersionWithProgressAsync(version, targetDirectory, progress, cancellationToken)
            : resourceCatalogService.DownloadProjectVersionAsync(version, targetDirectory, cancellationToken);

    private Task<string> InstallProjectVersionAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken) =>
        resourceCatalogService is IResourceCatalogProgressReporter progressReporter
            ? progressReporter.InstallProjectVersionWithProgressAsync(version, instance, progress, cancellationToken)
            : resourceCatalogService.InstallProjectVersionAsync(version, instance, cancellationToken);

    private async Task<ResourceProjectInstallationResult> ImportModpackAsNewInstanceAsync(
        ResourceProjectVersion version,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"{WorkspacePrefix}{transactionId}");
        Directory.CreateDirectory(tempDirectory);
        await using var activeLock = new FileStream(
            Path.Combine(tempDirectory, ActiveLockFileName),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        await AtomicJsonFileWriter.WriteAsync(
                Path.Combine(tempDirectory, MarkerFileName),
                new ResourceInstallWorkspaceMarker(1, transactionId),
                MarkerJsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var archivePath = await DownloadProjectVersionAsync(
                version,
                tempDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);
            var result = await localModpackImportService.ImportFromArchiveAsync(
                archivePath,
                progress,
                cancellationToken).ConfigureAwait(false);
            return new ResourceProjectInstallationResult(archivePath, result);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    await activeLock.DisposeAsync().ConfigureAwait(false);
                    DeleteOwnedWorkspace(tempDirectory, transactionId);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to clean resource project installation workspace. Workspace={Workspace}",
                    tempDirectory);
            }
        }
    }

    private void CleanupStaleWorkspaces(CancellationToken cancellationToken)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        foreach (var directory in Directory.EnumerateDirectories(tempRoot, $"{WorkspacePrefix}*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            var transactionId = name.StartsWith(WorkspacePrefix, StringComparison.OrdinalIgnoreCase)
                ? name[WorkspacePrefix.Length..]
                : string.Empty;
            if (!Guid.TryParseExact(transactionId, "N", out _)
                || !TryReadValidMarker(directory, transactionId))
            {
                continue;
            }

            FileStream? cleanupLock = null;
            try
            {
                cleanupLock = new FileStream(
                    Path.Combine(directory, ActiveLockFileName),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                if (TryReadValidMarker(directory, transactionId))
                    DeleteOwnedWorkspace(directory, transactionId, cleanupLock);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                logger.LogDebug("Resource install workspace is active in another process. Workspace={Workspace}", directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(exception, "Failed to clean stale resource install workspace. Workspace={Workspace}", directory);
            }
            finally
            {
                cleanupLock?.Dispose();
            }
        }
    }

    private static bool TryReadValidMarker(string directory, string transactionId)
    {
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return false;
            var marker = JsonSerializer.Deserialize<ResourceInstallWorkspaceMarker>(
                File.ReadAllText(Path.Combine(directory, MarkerFileName)),
                MarkerJsonOptions);
            return marker is { SchemaVersion: 1 }
                && string.Equals(marker.TransactionId, transactionId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void DeleteOwnedWorkspace(
        string directory,
        string transactionId,
        FileStream? cleanupLock = null)
    {
        if (!TryReadValidMarker(directory, transactionId))
            return;
        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            DeleteTreeWithoutFollowingReparsePoints(childDirectory);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (cleanupLock is not null
                && string.Equals(file, cleanupLock.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            File.Delete(file);
        }
        cleanupLock?.Dispose();
        var lockPath = Path.Combine(directory, ActiveLockFileName);
        if (File.Exists(lockPath))
            File.Delete(lockPath);
        Directory.Delete(directory, recursive: false);
    }

    private static void DeleteTreeWithoutFollowingReparsePoints(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, recursive: false);
            return;
        }
        foreach (var child in Directory.EnumerateDirectories(path))
            DeleteTreeWithoutFollowingReparsePoints(child);
        foreach (var file in Directory.EnumerateFiles(path))
            File.Delete(file);
        Directory.Delete(path, recursive: false);
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private sealed record ResourceInstallWorkspaceMarker(int SchemaVersion, string TransactionId);

    private static string RequireTargetDirectory(ResourceProjectInstallationRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.TargetDirectory)
            ? request.TargetDirectory
            : throw new ArgumentException("A target directory is required.", nameof(request));
    }

    private static GameInstance RequireInstance(ResourceProjectInstallationRequest request)
    {
        return request.Instance ?? throw new ArgumentException("An instance is required.", nameof(request));
    }
}
