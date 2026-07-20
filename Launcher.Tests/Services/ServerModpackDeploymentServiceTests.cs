/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services;

public sealed class ServerModpackDeploymentServiceTests
{
    [Theory]
    [InlineData("Better-MC-Server-30.zip", "30", "Better-MC-Server-30")]
    [InlineData("Bad<Name>.mrpack", "30", "Bad_Name_")]
    [InlineData("", "CON", "_CON")]
    [InlineData("", "...", "server")]
    public void DirectoryNameComesFromSanitizedArchiveFileName(
        string archiveFileName,
        string versionId,
        string expected)
    {
        Assert.Equal(expected, ServerDeploymentDirectoryName.Resolve(archiveFileName, versionId));
    }

    [Fact]
    public async Task ContentFailureCancelsRuntimeAndRollsBackTransaction()
    {
        var transaction = new FakeTransaction();
        var package = new FailingPackageService();
        var runtime = new CancelableRuntimeInstaller();
        var service = new ServerModpackDeploymentService(
            new FakeTransactionService(transaction),
            new NoOpExtractor(),
            package,
            runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeployAsync(
            new ServerModpackDeploymentRequest(
                "pack.mrpack",
                "parent",
                "pack.mrpack",
                "version",
                ResourceProjectSource.Modrinth)));

        Assert.True(runtime.CancellationObserved);
        Assert.True(transaction.Aborted);
        Assert.False(transaction.Committed);
        Assert.True(package.CleanedUp);
    }

    private sealed class FakeTransactionService(FakeTransaction transaction) : IServerDeploymentTransactionService
    {
        public Task<IServerDeploymentTransaction> BeginAsync(
            string parentDirectory,
            string directoryName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IServerDeploymentTransaction>(transaction);
    }

    private sealed class FakeTransaction : IServerDeploymentTransaction
    {
        public string StagingDirectory => "staging";
        public string FinalDirectory => "final";
        public bool Committed { get; private set; }
        public bool Aborted { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public Task AbortAsync(CancellationToken cancellationToken = default)
        {
            Aborted = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpExtractor : IServerPackExtractor
    {
        public Task ExtractAsync(
            string archivePath,
            string targetDirectory,
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CancelableRuntimeInstaller : IServerRuntimeInstaller
    {
        public bool CancellationObserved { get; private set; }

        public async Task InstallAsync(
            PreparedModpack modpack,
            string targetDirectory,
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class FailingPackageService : IModpackPackageService
    {
        private readonly PreparedModpack prepared = new()
        {
            PackageKind = ModpackPackageKind.Modrinth,
            Environment = ModpackInstallEnvironment.Server,
            MinecraftVersion = "1.20.1"
        };

        public bool CleanedUp { get; private set; }

        public Task<ModpackRecognitionResult> RecognizeAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ModpackRecognitionResult.Success());

        public Task<PreparedModpack> PrepareAsync(
            string archivePath,
            CancellationToken cancellationToken = default,
            IProgress<LauncherProgress>? progress = null) => Task.FromResult(prepared);

        public Task<PreparedModpack> PrepareAsync(
            string archivePath,
            ModpackInstallEnvironment environment,
            CancellationToken cancellationToken = default,
            IProgress<LauncherProgress>? progress = null) => Task.FromResult(prepared);

        public Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0) =>
            Task.FromException<IReadOnlyList<ManualModpackDownload>>(
                new InvalidOperationException("content failed"));

        public Task CopyOverridesAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> WriteManualDownloadsFileAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IReadOnlyList<ManualModpackDownload> manualDownloads,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task InstallContentAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0) => Task.CompletedTask;

        public Task CleanupAsync(
            PreparedModpack preparedModpack,
            CancellationToken cancellationToken = default)
        {
            CleanedUp = true;
            return Task.CompletedTask;
        }
    }
}
