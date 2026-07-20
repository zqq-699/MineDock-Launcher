/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed class ServerModpackDeploymentService : IServerModpackDeploymentService
{
    private readonly IServerDeploymentTransactionService transactionService;
    private readonly IServerPackExtractor serverPackExtractor;
    private readonly IModpackPackageService modpackPackageService;
    private readonly IServerRuntimeInstaller serverRuntimeInstaller;
    private readonly ILogger<ServerModpackDeploymentService> logger;

    public ServerModpackDeploymentService(
        IServerDeploymentTransactionService transactionService,
        IServerPackExtractor serverPackExtractor,
        IModpackPackageService modpackPackageService,
        IServerRuntimeInstaller serverRuntimeInstaller,
        ILogger<ServerModpackDeploymentService>? logger = null)
    {
        this.transactionService = transactionService;
        this.serverPackExtractor = serverPackExtractor;
        this.modpackPackageService = modpackPackageService;
        this.serverRuntimeInstaller = serverRuntimeInstaller;
        this.logger = logger ?? NullLogger<ServerModpackDeploymentService>.Instance;
    }

    public string ResolveTargetDirectory(string parentDirectory, string archiveFileName, string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        return Path.Combine(
            Path.GetFullPath(parentDirectory),
            ServerDeploymentDirectoryName.Resolve(archiveFileName, versionId));
    }

    public async Task<ServerModpackDeploymentResult> DeployAsync(
        ServerModpackDeploymentRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var directoryName = ServerDeploymentDirectoryName.Resolve(request.ArchiveFileName, request.VersionId);
        await using var transaction = await transactionService
            .BeginAsync(request.ParentDirectory, directoryName, cancellationToken)
            .ConfigureAwait(false);
        PreparedModpack? preparedModpack = null;
        var committed = false;
        try
        {
            logger.LogInformation(
                "Server modpack deployment started. Source={Source} VersionId={VersionId} DirectoryName={DirectoryName}",
                request.Source,
                request.VersionId,
                directoryName);

            if (request.Source is ResourceProjectSource.CurseForge)
            {
                await serverPackExtractor.ExtractAsync(
                    request.ArchivePath,
                    transaction.StagingDirectory,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (request.Source is ResourceProjectSource.Modrinth)
            {
                progress?.Report(new LauncherProgress(ImportProgressStages.ParsingManifest, string.Empty));
                preparedModpack = await modpackPackageService
                    .PrepareAsync(
                        request.ArchivePath,
                        ModpackInstallEnvironment.Server,
                        cancellationToken,
                        progress)
                    .ConfigureAwait(false);

                var targetInstance = new GameInstance
                {
                    Name = directoryName,
                    InstanceDirectory = transaction.StagingDirectory,
                    MinecraftVersion = preparedModpack.MinecraftVersion,
                    Loader = preparedModpack.Loader,
                    LoaderVersion = preparedModpack.LoaderVersion
                };
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var contentTask = modpackPackageService.DownloadFilesAsync(
                    preparedModpack,
                    targetInstance,
                    progress,
                    linkedCancellation.Token);
                var runtimeTask = serverRuntimeInstaller.InstallAsync(
                    preparedModpack,
                    transaction.StagingDirectory,
                    progress,
                    linkedCancellation.Token);
                try
                {
                    var firstCompleted = await Task.WhenAny(contentTask, runtimeTask).ConfigureAwait(false);
                    if (!firstCompleted.IsCompletedSuccessfully)
                        await linkedCancellation.CancelAsync().ConfigureAwait(false);
                    await Task.WhenAll(contentTask, runtimeTask).ConfigureAwait(false);
                }
                catch
                {
                    await linkedCancellation.CancelAsync().ConfigureAwait(false);
                    throw;
                }

                var manualDownloads = await contentTask.ConfigureAwait(false);
                if (manualDownloads.Count > 0)
                {
                    throw new ModpackImportException(
                        ModpackImportFailureReason.UnexpectedError,
                        "Server deployment requires files that cannot be downloaded automatically.");
                }

                await modpackPackageService.CopyOverridesAsync(
                    preparedModpack,
                    targetInstance,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException($"Unsupported server modpack source: {request.Source}");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            committed = true;
            logger.LogInformation(
                "Server modpack deployment completed. Source={Source} VersionId={VersionId} DirectoryName={DirectoryName}",
                request.Source,
                request.VersionId,
                directoryName);
            return new ServerModpackDeploymentResult(transaction.FinalDirectory);
        }
        finally
        {
            try
            {
                if (!committed)
                    await transaction.AbortAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (preparedModpack is not null)
                    await modpackPackageService.CleanupAsync(preparedModpack, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}

internal static class ServerDeploymentDirectoryName
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Resolve(string archiveFileName, string versionId)
    {
        var fileName = Path.GetFileName(archiveFileName ?? string.Empty);
        var name = Sanitize(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(name))
            name = Sanitize(versionId);
        if (string.IsNullOrWhiteSpace(name))
            name = "server";
        if (ReservedNames.Contains(name))
            name = $"_{name}";
        return name;
    }

    private static string Sanitize(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? string.Empty)
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray())
            .Trim()
            .TrimEnd('.', ' ');
    }
}
