/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class InstanceManagementViewModelTests : TestTempDirectory
{
    [Fact]
    public async Task RootChangeDuringRefreshDiscardsOldResultAndRefreshesNewRoot()
    {
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(TempRoot, "root-a")
        };
        var backupService = new RecordingBackupService();
        var instanceService = new RootSwitchingInstanceService(backupService);
        var viewModel = new InstanceManagementViewModel(
            new TestSettingsService(settings),
            instanceService,
            new NullStatusService(),
            backupService);

        var firstRefresh = viewModel.InitializeAsync(settings);
        await instanceService.FirstRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.MinecraftDirectory = Path.Combine(TempRoot, "root-b");
        var rootChangeRefresh = viewModel.RefreshInstancesAsync();
        instanceService.ReleaseFirstRefresh.TrySetResult();

        await Task.WhenAll(firstRefresh, rootChangeRefresh);

        Assert.Equal(2, instanceService.RefreshCount);
        Assert.Equal("root-b", Assert.Single(viewModel.Instances).Id);
        Assert.Equal(
            [Path.GetFullPath(Path.Combine(TempRoot, "root-a")), Path.GetFullPath(Path.Combine(TempRoot, "root-b"))],
            backupService.RecoveredDirectories);
    }

    private sealed class RootSwitchingInstanceService : IGameInstanceService
    {
        private readonly RecordingBackupService? backupService;
        private int refreshCount;

        public RootSwitchingInstanceService(RecordingBackupService? backupService = null)
        {
            this.backupService = backupService;
        }

        public int RefreshCount => Volatile.Read(ref refreshCount);
        public TaskCompletionSource FirstRefreshStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRefresh { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<GameInstance>> GetStoredInstancesAsync(
            LauncherSettings settings,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameInstance>>([]);

        public async Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref refreshCount);
            Assert.True(
                backupService is null || backupService.RecoveredDirectories.Count >= call,
                "Pending backup restores must be recovered before scanning an instance root.");
            if (call == 1)
            {
                FirstRefreshStarted.TrySetResult();
                await ReleaseFirstRefresh.Task.WaitAsync(cancellationToken);
                return [new GameInstance { Id = "root-a", Name = "A" }];
            }
            return [new GameInstance { Id = "root-b", Name = "B" }];
        }

        public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<GameInstance?>(null);

        public Task<GameInstance> CreateInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string? name,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0,
            bool installFabricApi = true,
            string? fabricApiVersionId = null,
            string? quiltStandardLibraryVersionId = null) => throw new NotSupportedException();

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GameInstance> RenameInstanceAsync(
            string instanceId,
            string? newName,
            string? newIconSource,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message) => MessageReported?.Invoke(message);
    }

    private sealed class RecordingBackupService : IInstanceBackupService
    {
        public List<string> RecoveredDirectories { get; } = [];

        public Task RecoverPendingRestoresAsync(
            string minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            RecoveredDirectories.Add(Path.GetFullPath(minecraftDirectory));
            return Task.CompletedTask;
        }

        public Task<string> EnsureBackupDirectoryAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountBackupEntriesAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InstanceBackupRecord> CreateBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteBackupAsync(
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RestoreBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
