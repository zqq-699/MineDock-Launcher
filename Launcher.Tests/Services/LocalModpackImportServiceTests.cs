/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services;

public sealed class LocalModpackImportServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ImportCompletesCorePipelineAndCleansWorkspace()
    {
        var package = new FakePackage(CreatePrepared("Fabric Pack"));
        var installer = new FakeInstaller();
        var staging = new FakeStaging(TempRoot);
        var service = new LocalModpackImportService(new FakeGameInstanceService(), package, installer, staging);

        var result = await service.ImportFromArchiveAsync("pack.mrpack", null);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, package.DownloadCount);
        Assert.Equal(1, installer.LoaderCount);
        Assert.Equal(1, staging.FinalizeCount);
        Assert.Equal(1, package.CleanupCount);
    }

    [Fact]
    public async Task CancellationUsesNonCanceledCleanupToken()
    {
        var package = new FakePackage(CreatePrepared("Canceled"))
        {
            DownloadException = new OperationCanceledException()
        };
        var staging = new FakeStaging(TempRoot);
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(), package, new FakeInstaller(), staging);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ImportFromArchiveAsync("pack.mrpack", null));

        Assert.Equal(1, package.CleanupCount);
        Assert.Equal(1, staging.CleanupCount);
        Assert.False(package.CleanupTokenCanBeCanceled);
        Assert.False(staging.CleanupTokenCanBeCanceled);
    }

    private PreparedModpack CreatePrepared(string name, ModpackPackageKind kind = ModpackPackageKind.Modrinth)
    {
        var work = Path.Combine(TempRoot, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return new PreparedModpack
        {
            PackageKind = kind,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = work,
            PackageName = name,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            LoaderVersion = "0.16.10"
        };
    }

    private sealed class FakePackage(PreparedModpack prepared) : IModpackPackageService
    {
        public Exception? DownloadException { get; init; }
        public int DownloadCount { get; private set; }
        public int CleanupCount { get; private set; }
        public bool CleanupTokenCanBeCanceled { get; private set; }
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ModpackRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(ModpackRecognitionResult.Success());
        public Task<PreparedModpack> PrepareAsync(string archivePath, CancellationToken cancellationToken = default,
            IProgress<LauncherProgress>? progress = null) => Task.FromResult(prepared);
        public Task InstallContentAsync(PreparedModpack modpack, GameInstance instance, IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default, DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0) => Task.CompletedTask;
        public Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(PreparedModpack modpack, GameInstance instance,
            IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference, int downloadSpeedLimitMbPerSecond = 0)
        {
            DownloadCount++;
            DownloadStarted.TrySetResult();
            if (DownloadException is not null) throw DownloadException;
            return Task.FromResult<IReadOnlyList<ManualModpackDownload>>(modpack.ManualDownloads);
        }
        public Task CopyOverridesAsync(PreparedModpack modpack, GameInstance instance, IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> WriteManualDownloadsFileAsync(PreparedModpack modpack, GameInstance instance,
            IReadOnlyList<ManualModpackDownload> downloads, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(downloads.Count == 0 ? null : Path.Combine(instance.InstanceDirectory, "manual.txt"));
        public Task CleanupAsync(PreparedModpack modpack, CancellationToken cancellationToken = default)
        {
            CleanupCount++;
            CleanupTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            if (Directory.Exists(modpack.WorkingDirectory)) Directory.Delete(modpack.WorkingDirectory, true);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstaller : IModpackGameInstaller
    {
        public Exception? LoaderException { get; init; }
        public int LoaderCount { get; private set; }
        public Task<string> InstallLoaderAsync(string minecraftVersion, LoaderKind loader, string? loaderVersion,
            LoaderInstallTarget target, IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default, DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LoaderCount++;
            if (LoaderException is not null)
                return Task.FromException<string>(LoaderException);
            return Task.FromResult(target.LogicalVersionName);
        }
        public Task<string> InstallInstanceAsync(string minecraftVersion, LoaderKind loader, string? loaderVersion,
            LoaderInstallTarget target, IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default, DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0) => Task.FromResult(target.LogicalVersionName);
    }

    private sealed class FakeStaging(string root) : IModpackInstanceStagingService
    {
        public int FinalizeCount { get; private set; }
        public int CleanupCount { get; private set; }
        public bool CleanupTokenCanBeCanceled { get; private set; }
        public string LastDirectory { get; private set; } = string.Empty;
        public Task<StagedModpackInstance> StageAsync(PreparedModpack modpack, string name, CancellationToken cancellationToken = default)
        {
            LastDirectory = Path.Combine(root, "versions", name);
            Directory.CreateDirectory(LastDirectory);
            return Task.FromResult(new StagedModpackInstance
            {
                ResolvedInstanceName = name,
                MinecraftDirectory = root,
                InstanceDirectory = LastDirectory,
                Instance = new GameInstance { Name = name, VersionName = name, InstanceDirectory = LastDirectory,
                    MinecraftVersion = modpack.MinecraftVersion, Loader = modpack.Loader, LoaderVersion = modpack.LoaderVersion }
            });
        }
        public Task<GameInstance> FinalizeAsync(StagedModpackInstance staged, string finalVersionName,
            CancellationToken cancellationToken = default)
        { FinalizeCount++; return Task.FromResult(staged.Instance); }
        public Task CleanupFailedImportAsync(StagedModpackInstance staged, string? finalVersionName,
            CancellationToken cancellationToken = default)
        {
            CleanupCount++;
            CleanupTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            if (Directory.Exists(staged.InstanceDirectory)) Directory.Delete(staged.InstanceDirectory, true);
            return Task.CompletedTask;
        }
    }
}
