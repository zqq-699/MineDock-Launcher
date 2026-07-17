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
    public async Task ImportReturnsManualDownloadsAsPartialSuccess()
    {
        var prepared = CreatePrepared("Curse Pack", ModpackPackageKind.CurseForge);
        prepared.ManualDownloads = [new ManualModpackDownload
        {
            ProjectId = 1,
            FileId = 2,
            FileName = "manual.jar",
            FailureSummary = "http_403"
        }];
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(), new FakePackage(prepared), new FakeInstaller(), new FakeStaging(TempRoot));

        var result = await service.ImportFromArchiveAsync("pack.zip", null);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsPartialSuccess);
        Assert.Single(result.ManualDownloads);
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

    [Fact]
    public async Task ContentFailureCleansStagedInstance()
    {
        var package = new FakePackage(CreatePrepared("Broken"))
        {
            DownloadException = new ModpackImportException(ModpackImportFailureReason.HashMismatch, "hash mismatch")
        };
        var staging = new FakeStaging(TempRoot);
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(), package, new FakeInstaller(), staging);

        var result = await service.ImportFromArchiveAsync("pack.mrpack", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.HashMismatch, result.FailureReason);
        Assert.Equal(1, staging.CleanupCount);
        Assert.False(Directory.Exists(staging.LastDirectory));
    }

    [Fact]
    public async Task JavaSelectionFailureReturnsDedicatedReasonAndCleansStagedInstance()
    {
        var staging = new FakeStaging(TempRoot);
        var installer = new FakeInstaller
        {
            LoaderException = new JavaRuntimeSelectionException(
                "missing java",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
                17)
        };
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(),
            new FakePackage(CreatePrepared("Missing Java")),
            installer,
            staging);

        var result = await service.ImportFromArchiveAsync("pack.mrpack", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.JavaRuntimeUnavailable, result.FailureReason);
        Assert.Equal(1, staging.CleanupCount);
        Assert.False(Directory.Exists(staging.LastDirectory));
    }

    [Fact]
    public async Task ConcurrentImportsSerializeLoaderInstallationButKeepContentDownloadsParallel()
    {
        var coordinator = new GameInstallCoordinator();
        var installer = new GatedInstaller();
        var firstPackage = new FakePackage(CreatePrepared("First Pack"));
        var secondPackage = new FakePackage(CreatePrepared("Second Pack"));
        var firstService = new LocalModpackImportService(
            new FakeGameInstanceService(),
            firstPackage,
            installer,
            new FakeStaging(TempRoot),
            coordinator);
        var secondService = new LocalModpackImportService(
            new FakeGameInstanceService(),
            secondPackage,
            installer,
            new FakeStaging(TempRoot),
            coordinator);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstImport = firstService.ImportFromArchiveAsync("first.mrpack", null);
        await installer.FirstInstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var secondImport = secondService.ImportFromArchiveAsync(
                "second.mrpack",
                new InlineProgress(progress =>
                {
                    if (progress.Stage == InstallProgressStages.Queue)
                        queued.TrySetResult();
                }));

            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondPackage.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, installer.LoaderCount);
            Assert.Equal(1, installer.MaxConcurrentInstallations);

            installer.ReleaseFirstInstall();
            var results = await Task.WhenAll(firstImport, secondImport).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.Equal(2, installer.LoaderCount);
            Assert.Equal(1, installer.MaxConcurrentInstallations);
        }
        finally
        {
            installer.ReleaseFirstInstall();
        }
    }

    [Fact]
    public async Task WaitingImportCanBeCanceledWithoutStartingLoaderInstallation()
    {
        var coordinator = new GameInstallCoordinator();
        var installer = new FakeInstaller();
        var package = new FakePackage(CreatePrepared("Queued Pack"));
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(),
            package,
            installer,
            new FakeStaging(TempRoot),
            coordinator);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldLease = await coordinator.AcquireInstallAsync(TempRoot, "Normal Instance", null);
        using var cancellation = new CancellationTokenSource();

        try
        {
            var import = service.ImportFromArchiveAsync(
                "queued.mrpack",
                new InlineProgress(progress =>
                {
                    if (progress.Stage == InstallProgressStages.Queue)
                        queued.TrySetResult();
                }),
                cancellation.Token);

            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await package.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => import.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, installer.LoaderCount);
        }
        finally
        {
            await heldLease.DisposeAsync();
        }
    }

    [Fact]
    public async Task LoaderFailureReleasesInstallLease()
    {
        var coordinator = new GameInstallCoordinator();
        var installer = new FakeInstaller
        {
            LoaderException = new InvalidOperationException("loader failed")
        };
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(),
            new FakePackage(CreatePrepared("Broken Loader")),
            installer,
            new FakeStaging(TempRoot),
            coordinator);

        var result = await service.ImportFromArchiveAsync("broken.mrpack", null);

        Assert.False(result.IsSuccess);
        await AssertCoordinatorAvailableAsync(coordinator, TempRoot);
    }

    [Fact]
    public async Task LoaderCancellationReleasesInstallLease()
    {
        var coordinator = new GameInstallCoordinator();
        var installer = new FakeInstaller
        {
            LoaderException = new OperationCanceledException()
        };
        var service = new LocalModpackImportService(
            new FakeGameInstanceService(),
            new FakePackage(CreatePrepared("Canceled Loader")),
            installer,
            new FakeStaging(TempRoot),
            coordinator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ImportFromArchiveAsync("canceled.mrpack", null));

        await AssertCoordinatorAvailableAsync(coordinator, TempRoot);
    }

    private static async Task AssertCoordinatorAvailableAsync(
        IGameInstallCoordinator coordinator,
        string minecraftDirectory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var lease = await coordinator.AcquireInstallAsync(
            minecraftDirectory,
            "Next Instance",
            null,
            timeout.Token);
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

    private sealed class GatedInstaller : IModpackGameInstaller
    {
        private readonly TaskCompletionSource releaseFirstInstall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeInstallations;
        private int loaderCount;
        private int maxConcurrentInstallations;

        public TaskCompletionSource FirstInstallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LoaderCount => Volatile.Read(ref loaderCount);
        public int MaxConcurrentInstallations => Volatile.Read(ref maxConcurrentInstallations);

        public async Task<string> InstallLoaderAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            LoaderInstallTarget target,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            var call = Interlocked.Increment(ref loaderCount);
            var active = Interlocked.Increment(ref activeInstallations);
            UpdateMaximum(ref maxConcurrentInstallations, active);
            try
            {
                if (call == 1)
                {
                    FirstInstallStarted.TrySetResult();
                    await releaseFirstInstall.Task.WaitAsync(cancellationToken);
                }

                return target.LogicalVersionName;
            }
            finally
            {
                Interlocked.Decrement(ref activeInstallations);
            }
        }

        public Task<string> InstallInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            LoaderInstallTarget target,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            int downloadSpeedLimitMbPerSecond = 0) => Task.FromResult(target.LogicalVersionName);

        public void ReleaseFirstInstall() => releaseFirstInstall.TrySetResult();

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximum);
                if (candidate <= current || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
                    return;
            }
        }
    }

    private sealed class InlineProgress(Action<LauncherProgress> report) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => report(value);
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
