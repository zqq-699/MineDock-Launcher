/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public enum ModpackInstallEnvironment
{
    Client,
    Server
}

public sealed record ServerModpackDeploymentRequest(
    string ArchivePath,
    string ParentDirectory,
    string ArchiveFileName,
    string VersionId,
    ResourceProjectSource Source);

public sealed record ServerModpackDeploymentResult(string FinalDirectory);

public sealed class ServerDeploymentDirectoryExistsException(string directory) : IOException(
    $"The server deployment directory already exists: {directory}")
{
    public string Directory { get; } = directory;
}

public interface IServerModpackDeploymentService
{
    string ResolveTargetDirectory(string parentDirectory, string archiveFileName, string versionId);

    Task<ServerModpackDeploymentResult> DeployAsync(
        ServerModpackDeploymentRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IServerDeploymentTransactionService
{
    Task<IServerDeploymentTransaction> BeginAsync(
        string parentDirectory,
        string directoryName,
        CancellationToken cancellationToken = default);
}

public interface IServerDeploymentTransaction : IAsyncDisposable
{
    string StagingDirectory { get; }

    string FinalDirectory { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task AbortAsync(CancellationToken cancellationToken = default);
}

public interface IServerPackExtractor
{
    Task ExtractAsync(
        string archivePath,
        string targetDirectory,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IServerRuntimeInstaller
{
    Task InstallAsync(
        PreparedModpack modpack,
        string targetDirectory,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
