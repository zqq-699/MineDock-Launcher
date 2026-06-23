using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Services;

public sealed class LocalModpackImportServiceTests : TestTempDirectory
{
    [Fact]
    public async Task LocalModpackRecognitionSucceedsAndCleansWorkspace()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Recognized Pack"));
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));

        var result = await service.RecognizeArchiveAsync(Path.Combine(TempRoot, "recognized.mrpack"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, packageService.RecognizeCallCount);
        Assert.Equal(0, packageService.CleanupCallCount);
    }

    [Fact]
    public async Task LocalModpackRecognitionMapsKnownFailureReason()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Broken Pack"))
        {
            RecognizeResultToReturn = ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.InvalidManifest)
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));

        var result = await service.RecognizeArchiveAsync(Path.Combine(TempRoot, "broken.zip"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackRecognitionFailureReason.InvalidManifest, result.FailureReason);
        Assert.Equal(0, packageService.CleanupCallCount);
    }

    [Fact]
    public async Task LocalModpackImportCreatesInstanceAndSkipsFabricApiInstall()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Fabric Pack"));
        var installer = new FakeModpackGameInstaller();
        var stagingService = new FakeModpackInstanceStagingService(TempRoot);
        var service = new LocalModpackImportService(instanceService, packageService, installer, stagingService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "fabric-pack.mrpack"), progress: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fabric Pack", stagingService.LastResolvedInstanceName);
        Assert.Equal(1, packageService.DownloadFilesCallCount);
        Assert.Equal(1, installer.InstallMinecraftBaseCallCount);
        Assert.Equal(1, installer.InstallLoaderCallCount);
        Assert.Equal(1, stagingService.FinalizeCallCount);
        Assert.Equal(1, packageService.CleanupCallCount);
        Assert.False(Directory.Exists(packageService.PreparedModpack.WorkingDirectory));
    }

    [Fact]
    public async Task LocalModpackImportCreatesNeoForgeInstance()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack(
            "NeoForge Pack",
            loader: LoaderKind.NeoForge,
            loaderVersion: "20.4.237",
            minecraftVersion: "1.20.4"));
        var installer = new FakeModpackGameInstaller();
        var stagingService = new FakeModpackInstanceStagingService(TempRoot);
        var service = new LocalModpackImportService(instanceService, packageService, installer, stagingService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "neoforge-pack.mrpack"), progress: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("NeoForge Pack", stagingService.LastResolvedInstanceName);
        Assert.Equal(1, installer.InstallMinecraftBaseCallCount);
        Assert.Equal(1, installer.InstallLoaderCallCount);
        Assert.Equal(1, stagingService.FinalizeCallCount);
        Assert.Equal(1, packageService.CleanupCallCount);
        Assert.False(Directory.Exists(packageService.PreparedModpack.WorkingDirectory));
    }

    [Fact]
    public async Task LocalModpackImportReturnsSuccessWithManualDownloadsWhenCurseForgeFilesAreSkipped()
    {
        var instanceService = new FakeGameInstanceService();
        var preparedModpack = CreatePreparedModpack("Curse Pack", ModpackPackageKind.CurseForge);
        var packageService = new FakeModpackPackageService(preparedModpack)
        {
            InstallCallback = prepared =>
            {
                prepared.ManualDownloads =
                [
                    new ManualModpackDownload
                    {
                        ProjectId = 348025,
                        FileId = 4436467,
                        FileName = "SRParasites-1.12.2v1.9.11.jar",
                        DisplayName = "SRP v 1.9.11",
                        SuggestedUrl = "https://edge.forgecdn.net/files/4436/467/SRParasites-1.12.2v1.9.11.jar",
                        FailureSummary = "http_403"
                    }
                ];
            }
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "curse-pack.zip"), progress: null);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsPartialSuccess);
        Assert.True(result.HasManualDownloads);
        Assert.Single(result.ManualDownloads);
        Assert.Equal(0, instanceService.DeleteCallCount);
    }

    [Fact]
    public async Task LocalModpackImportAddsSuffixWhenInstanceNameAlreadyExists()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(new GameInstance { Name = "Imported Pack", VersionName = "Imported Pack" });
        instanceService.CreatedInstances.Add(new GameInstance { Name = "Imported Pack (1)", VersionName = "Imported Pack (1)" });
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Imported Pack"));
        var stagingService = new FakeModpackInstanceStagingService(TempRoot);
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            stagingService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "imported-pack.mrpack"), progress: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Imported Pack (2)", stagingService.LastResolvedInstanceName);
    }

    [Fact]
    public async Task LocalModpackImportDoesNotDiscoverHalfPublishedVersionDuringFinalize()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var coordinator = new GameInstallCoordinator();
        var instanceService = new GameInstanceService(
            settingsService,
            repository,
            [new FakeLoaderProvider()],
            installCoordinator: coordinator);
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Half Published Pack"));
        var installer = new FakeModpackGameInstaller { WriteVersionJsonDuringLoaderInstall = true };
        var stagingService = new ModpackInstanceStagingService(settingsService, repository, instanceService);
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            installer,
            stagingService,
            coordinator);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "half-published-pack.mrpack"), progress: null);

        Assert.True(result.IsSuccess);
        var storedInstance = Assert.Single(await repository.GetAllAsync());
        Assert.Equal("Half Published Pack", storedInstance.Name);
        Assert.Equal("Half Published Pack", storedInstance.VersionName);
        Assert.False(storedInstance.Id.StartsWith("local-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.ImportedInstance?.Id, storedInstance.Id);
        Assert.Equal(storedInstance.Id, (await settingsService.LoadAsync()).DefaultInstanceId);
    }

    [Fact]
    public async Task LocalModpackImportQueuesBehindGameInstanceCreateAndPersistsWithoutDuplicates()
    {
        var settings = new LauncherSettings
        {
            DataDirectory = TempRoot,
            MinecraftDirectory = Path.Combine(TempRoot, ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var repository = new JsonGameInstanceRepository(settingsService);
        var coordinator = new GameInstallCoordinator();
        var allowCreateInstall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeLoaderProvider { WaitBeforeInstall = allowCreateInstall.Task };
        var instanceService = new GameInstanceService(
            settingsService,
            repository,
            [provider],
            installCoordinator: coordinator);
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Queued Import Pack"));
        var installer = new FakeModpackGameInstaller { WriteVersionJsonDuringLoaderInstall = true };
        var stagingService = new ModpackInstanceStagingService(settingsService, repository, instanceService);
        var importService = new LocalModpackImportService(
            instanceService,
            packageService,
            installer,
            stagingService,
            coordinator);
        var importProgress = new ProgressCollector();

        var createTask = instanceService.CreateInstanceAsync("1.20.1", LoaderKind.Vanilla, null, "Normal Install", progress: null);
        await provider.InstallStarted.Task;

        var importTask = importService.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "queued-import-pack.mrpack"),
            importProgress);
        await TestAsync.WaitForAsync(() => importProgress.Values.Any(value => value.Stage == InstallProgressStages.Queue));
        Assert.Equal(0, installer.InstallMinecraftBaseCallCount);
        Assert.Equal(1, packageService.DownloadFilesCallCount);

        allowCreateInstall.SetResult(true);
        await Task.WhenAll(createTask, importTask);

        var storedInstances = await repository.GetAllAsync();
        Assert.Equal(2, storedInstances.Count);
        Assert.Contains(storedInstances, instance => instance.Name == "Normal Install");
        Assert.Contains(storedInstances, instance => instance.Name == "Queued Import Pack");
        Assert.Equal(storedInstances.Count, storedInstances.Select(instance => instance.VersionName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(storedInstances, instance => instance.Id.StartsWith("local-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LocalModpackImportCleansUpWithNonCanceledTokenAfterCancellation()
    {
        var instanceService = new FakeGameInstanceService();
        var preparedModpack = CreatePreparedModpack("Canceled Pack");
        var packageService = new FakeModpackPackageService(preparedModpack)
        {
            InstallException = new OperationCanceledException()
        };
        var installer = new FakeModpackGameInstaller();
        var stagingService = new FakeModpackInstanceStagingService(TempRoot);
        var service = new LocalModpackImportService(instanceService, packageService, installer, stagingService);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ImportFromArchiveAsync(Path.Combine(TempRoot, "canceled-pack.mrpack"), progress: null));

        Assert.Equal(1, packageService.CleanupCallCount);
        Assert.Equal(1, stagingService.CleanupCallCount);
        Assert.False(packageService.LastCleanupTokenCanBeCanceled);
        Assert.False(stagingService.LastCleanupTokenCanBeCanceled);
    }

    [Fact]
    public async Task LocalModpackImportDeletesCreatedInstanceWhenContentInstallFails()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Broken Pack"))
        {
            InstallException = new ModpackImportException(ModpackImportFailureReason.HashMismatch, "hash mismatch")
        };
        var stagingService = new FakeModpackInstanceStagingService(TempRoot);
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            stagingService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "broken-pack.mrpack"), progress: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.HashMismatch, result.FailureReason);
        Assert.Equal(0, instanceService.DeleteCallCount);
        Assert.Equal(1, stagingService.CleanupCallCount);
        Assert.Equal(1, packageService.CleanupCallCount);
        Assert.False(Directory.Exists(packageService.PreparedModpack.WorkingDirectory));
    }

    [Fact]
    public async Task LocalModpackImportCleansWorkspaceWhenCreateInstanceFails()
    {
        var instanceService = new FakeGameInstanceService
        {
            CreateException = new InvalidOperationException("create failed")
        };
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Create Fail Pack"));
        var stagingService = new FakeModpackInstanceStagingService(TempRoot)
        {
            StageException = new InvalidOperationException("create failed")
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            stagingService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "create-fail.mrpack"), progress: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.UnexpectedError, result.FailureReason);
        Assert.Equal(0, instanceService.DeleteCallCount);
        Assert.Equal(1, packageService.CleanupCallCount);
        Assert.False(Directory.Exists(packageService.PreparedModpack.WorkingDirectory));
    }

    [Fact]
    public async Task LocalModpackImportDoesNotReportCleanupWhenFailureOccursBeforePreparedWorkspaceExists()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Missing Key Pack"))
        {
            PrepareException = new ModpackImportException(
                ModpackImportFailureReason.MissingCurseForgeApiKey,
                "missing key")
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));
        var progress = new ProgressCollector();

        var result = await service.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "missing-key.zip"),
            progress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.MissingCurseForgeApiKey, result.FailureReason);
        Assert.DoesNotContain(progress.Values, value => value.Stage == ImportProgressStages.CleaningUp);
        Assert.Equal(0, packageService.CleanupCallCount);
    }

    [Fact]
    public async Task LocalModpackImportReportsCleanupWhenPreparedWorkspaceMustBeRemoved()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Cleanup Pack"))
        {
            InstallException = new ModpackImportException(ModpackImportFailureReason.HashMismatch, "hash mismatch")
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));
        var progress = new ProgressCollector();

        var result = await service.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "cleanup-pack.mrpack"),
            progress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.HashMismatch, result.FailureReason);
        Assert.Contains(progress.Values, value => value.Stage == ImportProgressStages.CleaningUp);
        Assert.Equal(1, packageService.CleanupCallCount);
    }

    [Fact]
    public async Task LocalModpackImportSurfacesPrepareProgressUpdates()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Progress Pack"))
        {
            PrepareCallback = progress => progress?.Report(
                new LauncherProgress(ImportProgressStages.ResolvingPackFiles, "1/3", 33))
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));
        var progress = new ProgressCollector();

        var result = await service.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "progress-pack.zip"),
            progress);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            progress.Values,
            value => value.Stage == ImportProgressStages.ResolvingPackFiles
                && value.Message == "1/3"
                && value.Percent is > 5 and < 25);
    }

    [Fact]
    public async Task LocalModpackImportReportsMonotonicOverallProgress()
    {
        var instanceService = new FakeGameInstanceService
        {
            InitialProgress = new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty, 100)
        };
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Overall Progress Pack"))
        {
            PrepareCallback = progress => progress?.Report(
                new LauncherProgress(ImportProgressStages.ResolvingPackFiles, "3/3", 100)),
            InstallProgressCallback = progress => progress?.Report(
                new LauncherProgress(ImportProgressStages.DownloadingPackFiles, "final.jar", 100))
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            new FakeModpackGameInstaller(),
            new FakeModpackInstanceStagingService(TempRoot));
        var progress = new ProgressCollector();

        var result = await service.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "overall-progress-pack.zip"),
            progress);

        Assert.True(result.IsSuccess);
        var reportedPercents = progress.Values
            .Where(value => value.Percent is not null)
            .Select(value => value.Percent!.Value)
            .ToList();

        Assert.NotEmpty(reportedPercents);
        Assert.True(reportedPercents.SequenceEqual(reportedPercents.OrderBy(value => value)));
        Assert.All(reportedPercents, value => Assert.InRange(value, 0, 99));
    }

    [Fact]
    public async Task LocalModpackImportDoesNotReachNinetyNinePercentBeforeInstallProgressFinishes()
    {
        var instanceService = new FakeGameInstanceService();
        var installer = new FakeModpackGameInstaller
        {
            LoaderInstallProgressToReport = new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty, 10)
        };
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Weighted Progress Pack"))
        {
            PrepareCallback = progress => progress?.Report(
                new LauncherProgress(ImportProgressStages.ResolvingPackFiles, "3/3", 100)),
            InstallProgressCallback = progress => progress?.Report(
                new LauncherProgress(ImportProgressStages.DownloadingPackFiles, "final.jar", 100))
        };
        var service = new LocalModpackImportService(
            instanceService,
            packageService,
            installer,
            new FakeModpackInstanceStagingService(TempRoot));
        var progress = new ProgressCollector();

        var result = await service.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "weighted-progress-pack.zip"),
            progress);

        Assert.True(result.IsSuccess);
        var maxReportedPercent = progress.Values
            .Where(value => value.Percent is not null)
            .Max(value => value.Percent!.Value);
        Assert.InRange(maxReportedPercent, 1, 98.99);
    }

    private PreparedModpack CreatePreparedModpack(
        string packageName,
        ModpackPackageKind packageKind = ModpackPackageKind.Modrinth,
        LoaderKind loader = LoaderKind.Fabric,
        string? loaderVersion = "0.16.10",
        string minecraftVersion = "1.20.1")
    {
        var workingDirectory = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        return new PreparedModpack
        {
            PackageKind = packageKind,
            SourceArchivePath = Path.Combine(TempRoot, $"{packageName}.mrpack"),
            WorkingDirectory = workingDirectory,
            PackageName = packageName,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader is LoaderKind.Vanilla ? null : loaderVersion
        };
    }

    private sealed class FakeModpackPackageService : IModpackPackageService
    {
        public FakeModpackPackageService(PreparedModpack preparedModpack)
        {
            PreparedModpack = preparedModpack;
        }

        public PreparedModpack PreparedModpack { get; }

        public Exception? InstallException { get; init; }

        public Exception? PrepareException { get; init; }

        public Action<PreparedModpack>? InstallCallback { get; init; }

        public Action<IProgress<LauncherProgress>?>? PrepareCallback { get; init; }

        public Action<IProgress<LauncherProgress>?>? InstallProgressCallback { get; init; }

        public ModpackRecognitionResult RecognizeResultToReturn { get; init; } = ModpackRecognitionResult.Success();

        public int InstallContentCallCount { get; private set; }

        public int DownloadFilesCallCount { get; private set; }

        public int CleanupCallCount { get; private set; }

        public int RecognizeCallCount { get; private set; }

        public bool? LastCleanupTokenCanBeCanceled { get; private set; }

        public Task<ModpackRecognitionResult> RecognizeAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            RecognizeCallCount++;
            return Task.FromResult(RecognizeResultToReturn);
        }

        public Task<PreparedModpack> PrepareAsync(
            string archivePath,
            CancellationToken cancellationToken = default,
            IProgress<LauncherProgress>? progress = null)
        {
            if (PrepareException is not null)
                throw PrepareException;

            PrepareCallback?.Invoke(progress);
            return Task.FromResult(PreparedModpack);
        }

        public Task InstallContentAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            InstallContentCallCount++;
            if (InstallException is not null)
                throw InstallException;

            InstallProgressCallback?.Invoke(progress);
            InstallCallback?.Invoke(preparedModpack);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            DownloadFilesCallCount++;
            InstallContentCallCount++;
            if (InstallException is not null)
                throw InstallException;

            InstallProgressCallback?.Invoke(progress);
            InstallCallback?.Invoke(preparedModpack);
            return Task.FromResult(preparedModpack.ManualDownloads);
        }

        public Task CopyOverridesAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string?> WriteManualDownloadsFileAsync(
            PreparedModpack preparedModpack,
            GameInstance instance,
            IReadOnlyList<ManualModpackDownload> manualDownloads,
            CancellationToken cancellationToken = default)
        {
            if (manualDownloads.Count <= 0)
                return Task.FromResult<string?>(null);

            Directory.CreateDirectory(instance.InstanceDirectory);
            var filePath = Path.Combine(instance.InstanceDirectory, ModpackManualDownloads.FileName);
            File.WriteAllText(filePath, "stub");
            return Task.FromResult<string?>(filePath);
        }

        public Task CleanupAsync(PreparedModpack preparedModpack, CancellationToken cancellationToken = default)
        {
            CleanupCallCount++;
            LastCleanupTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            if (Directory.Exists(preparedModpack.WorkingDirectory))
                Directory.Delete(preparedModpack.WorkingDirectory, recursive: true);

            return Task.CompletedTask;
        }
    }

    private sealed class ProgressCollector : IProgress<LauncherProgress>
    {
        private readonly object syncRoot = new();
        private readonly List<LauncherProgress> values = [];

        public IReadOnlyList<LauncherProgress> Values
        {
            get
            {
                lock (syncRoot)
                    return values.ToList();
            }
        }

        public void Report(LauncherProgress value)
        {
            lock (syncRoot)
                values.Add(value);
        }
    }

    private sealed class FakeModpackGameInstaller : IModpackGameInstaller
    {
        public int InstallMinecraftBaseCallCount { get; private set; }

        public int InstallLoaderCallCount { get; private set; }

        public LauncherProgress? BaseInstallProgressToReport { get; init; }

        public LauncherProgress? LoaderInstallProgressToReport { get; init; }

        public bool WriteVersionJsonDuringLoaderInstall { get; init; }

        public Task InstallMinecraftBaseAsync(
            string minecraftVersion,
            string gameDirectory,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            InstallMinecraftBaseCallCount++;
            progress?.Report(BaseInstallProgressToReport ?? new LauncherProgress(InstallProgressStages.Preparing, string.Empty, 50));
            return Task.CompletedTask;
        }

        public Task<string> InstallLoaderAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string gameDirectory,
            string isolatedVersionName,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            InstallLoaderCallCount++;
            if (WriteVersionJsonDuringLoaderInstall)
                WriteInstalledVersion(gameDirectory, isolatedVersionName, minecraftVersion);

            progress?.Report(LoaderInstallProgressToReport ?? new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty, 100));
            return Task.FromResult(isolatedVersionName);
        }

        public Task<string> InstallInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string gameDirectory,
            string isolatedVersionName,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromResult(isolatedVersionName);
        }

        private static void WriteInstalledVersion(string minecraftDirectory, string versionName, string minecraftVersion)
        {
            var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(
                Path.Combine(versionDirectory, $"{versionName}.json"),
                $$"""
                {
                  "id": "{{versionName}}",
                  "jar": "{{versionName}}",
                  "type": "release",
                  "launcher": {
                    "minecraftVersion": "{{minecraftVersion}}"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.jar"), "fake jar");
        }
    }

    private sealed class FakeModpackInstanceStagingService(string tempRoot) : IModpackInstanceStagingService
    {
        public string? LastResolvedInstanceName { get; private set; }

        public int FinalizeCallCount { get; private set; }

        public int CleanupCallCount { get; private set; }

        public bool? LastCleanupTokenCanBeCanceled { get; private set; }

        public Exception? StageException { get; init; }

        public Task<StagedModpackInstance> StageAsync(
            PreparedModpack preparedModpack,
            string resolvedInstanceName,
            CancellationToken cancellationToken = default)
        {
            if (StageException is not null)
                throw StageException;

            LastResolvedInstanceName = resolvedInstanceName;
            var instanceDirectory = Path.Combine(preparedModpack.WorkingDirectory, "instance-content");
            Directory.CreateDirectory(instanceDirectory);
            return Task.FromResult(new StagedModpackInstance
            {
                ResolvedInstanceName = resolvedInstanceName,
                MinecraftDirectory = tempRoot,
                StagingContentDirectory = instanceDirectory,
                Instance = new GameInstance
                {
                    Name = resolvedInstanceName,
                    MinecraftVersion = preparedModpack.MinecraftVersion,
                    Loader = preparedModpack.Loader,
                    LoaderVersion = preparedModpack.LoaderVersion,
                    VersionName = resolvedInstanceName,
                    InstanceDirectory = instanceDirectory
                }
            });
        }

        public Task<GameInstance> FinalizeAsync(
            StagedModpackInstance stagedInstance,
            string finalVersionName,
            CancellationToken cancellationToken = default)
        {
            FinalizeCallCount++;
            stagedInstance.Instance.VersionName = finalVersionName;
            stagedInstance.Instance.InstanceDirectory = Path.Combine(tempRoot, finalVersionName);
            Directory.CreateDirectory(stagedInstance.Instance.InstanceDirectory);
            return Task.FromResult(stagedInstance.Instance);
        }

        public Task CleanupFailedImportAsync(
            StagedModpackInstance stagedInstance,
            string? finalVersionName,
            CancellationToken cancellationToken = default)
        {
            CleanupCallCount++;
            LastCleanupTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            if (Directory.Exists(stagedInstance.StagingContentDirectory))
                Directory.Delete(stagedInstance.StagingContentDirectory, recursive: true);

            return Task.CompletedTask;
        }
    }
}
