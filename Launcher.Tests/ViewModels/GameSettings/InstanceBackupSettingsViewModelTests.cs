/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class InstanceBackupSettingsViewModelTests : TestTempDirectory
{
    [Fact]
    public async Task CreatingBackupIsTrackedUntilTheEntireOperationCompletes()
    {
        var backupDirectory = Path.Combine(TempRoot, "backups");
        var backupService = new BlockingBackupService();
        var trackedTasks = new DownloadTasksPageViewModel();
        var viewModel = new InstanceBackupSettingsViewModel(
            null!,
            Stub<IGameInstanceService>(),
            backupService,
            trackedTasks,
            new NullStatusService(),
            Stub<IInstanceFolderService>(),
            Stub<IFilePickerService>(),
            new NullFloatingMessageService());
        viewModel.OnSelectedInstanceChanged(new GameInstance
        {
            Id = "test",
            Name = "Test",
            VersionName = "Test",
            InstanceDirectory = Path.Combine(TempRoot, "versions", "Test"),
            BackupDirectory = backupDirectory
        });
        viewModel.NewBackupName = "manual";

        var operation = viewModel.ConfirmCreateBackupDialogCommand.ExecuteAsync(null);
        await backupService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shutdownWait = trackedTasks.WaitForTrackedBackgroundTasksAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, trackedTasks.TrackedBackgroundTaskCount);
        Assert.False(shutdownWait.IsCompleted);

        backupService.ReleaseCreate.TrySetResult();
        await operation;

        Assert.True(await shutdownWait);
    }

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, DefaultInterfaceProxy>();

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            if (returnType is null || returnType == typeof(void))
                return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private sealed class BlockingBackupService : IInstanceBackupService
    {
        public TaskCompletionSource CreateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCreate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> EnsureBackupDirectoryAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default) => Task.FromResult(backupDirectory);

        public Task<int> CountBackupEntriesAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstanceBackupRecord>>([]);

        public async Task<InstanceBackupRecord> CreateBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupName,
            CancellationToken cancellationToken = default)
        {
            CreateStarted.TrySetResult();
            await ReleaseCreate.Task.WaitAsync(cancellationToken);
            return new InstanceBackupRecord
            {
                Name = backupName,
                FileName = $"{backupName}.zip",
                FullPath = Path.Combine(backupDirectory, $"{backupName}.zip")
            };
        }

        public Task DeleteBackupAsync(
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestoreBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecoverPendingRestoresAsync(
            string minecraftDirectory,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message) => MessageReported?.Invoke(message);
    }

    private sealed class NullFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public void Show(string message) => MessageRequested?.Invoke(message);
    }
}
