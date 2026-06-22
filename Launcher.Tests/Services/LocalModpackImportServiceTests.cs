using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services;

public sealed class LocalModpackImportServiceTests : TestTempDirectory
{
    [Fact]
    public async Task LocalModpackRecognitionSucceedsAndCleansWorkspace()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Recognized Pack"));
        var service = new LocalModpackImportService(instanceService, packageService);

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
        var service = new LocalModpackImportService(instanceService, packageService);

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
        var service = new LocalModpackImportService(instanceService, packageService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "fabric-pack.mrpack"), progress: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fabric Pack", instanceService.LastName);
        Assert.False(instanceService.LastInstallFabricApi);
        Assert.Equal(1, packageService.InstallContentCallCount);
        Assert.Equal(1, packageService.CleanupCallCount);
        Assert.False(Directory.Exists(packageService.PreparedModpack.WorkingDirectory));
    }

    [Fact]
    public async Task LocalModpackImportAddsSuffixWhenInstanceNameAlreadyExists()
    {
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(new GameInstance { Name = "Imported Pack", VersionName = "Imported Pack" });
        instanceService.CreatedInstances.Add(new GameInstance { Name = "Imported Pack (1)", VersionName = "Imported Pack (1)" });
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Imported Pack"));
        var service = new LocalModpackImportService(instanceService, packageService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "imported-pack.mrpack"), progress: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Imported Pack (2)", instanceService.LastName);
    }

    [Fact]
    public async Task LocalModpackImportDeletesCreatedInstanceWhenContentInstallFails()
    {
        var instanceService = new FakeGameInstanceService();
        var packageService = new FakeModpackPackageService(CreatePreparedModpack("Broken Pack"))
        {
            InstallException = new ModpackImportException(ModpackImportFailureReason.HashMismatch, "hash mismatch")
        };
        var service = new LocalModpackImportService(instanceService, packageService);

        var result = await service.ImportFromArchiveAsync(Path.Combine(TempRoot, "broken-pack.mrpack"), progress: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.HashMismatch, result.FailureReason);
        Assert.Equal(1, instanceService.DeleteCallCount);
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
        var service = new LocalModpackImportService(instanceService, packageService);

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
        var service = new LocalModpackImportService(instanceService, packageService);
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
        var service = new LocalModpackImportService(instanceService, packageService);
        var progress = new ProgressCollector();

        var result = await service.ImportFromArchiveAsync(
            Path.Combine(TempRoot, "cleanup-pack.mrpack"),
            progress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackImportFailureReason.HashMismatch, result.FailureReason);
        Assert.Contains(progress.Values, value => value.Stage == ImportProgressStages.CleaningUp);
        Assert.Equal(1, packageService.CleanupCallCount);
    }

    private PreparedModpack CreatePreparedModpack(string packageName)
    {
        var workingDirectory = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        return new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, $"{packageName}.mrpack"),
            WorkingDirectory = workingDirectory,
            PackageName = packageName,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            LoaderVersion = "0.16.10"
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

        public ModpackRecognitionResult RecognizeResultToReturn { get; init; } = ModpackRecognitionResult.Success();

        public int InstallContentCallCount { get; private set; }

        public int CleanupCallCount { get; private set; }

        public int RecognizeCallCount { get; private set; }

        public Task<ModpackRecognitionResult> RecognizeAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            RecognizeCallCount++;
            return Task.FromResult(RecognizeResultToReturn);
        }

        public Task<PreparedModpack> PrepareAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            if (PrepareException is not null)
                throw PrepareException;

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

            return Task.CompletedTask;
        }

        public Task CleanupAsync(PreparedModpack preparedModpack, CancellationToken cancellationToken = default)
        {
            CleanupCallCount++;
            if (Directory.Exists(preparedModpack.WorkingDirectory))
                Directory.Delete(preparedModpack.WorkingDirectory, recursive: true);

            return Task.CompletedTask;
        }
    }

    private sealed class ProgressCollector : IProgress<LauncherProgress>
    {
        public List<LauncherProgress> Values { get; } = [];

        public void Report(LauncherProgress value)
        {
            Values.Add(value);
        }
    }
}
