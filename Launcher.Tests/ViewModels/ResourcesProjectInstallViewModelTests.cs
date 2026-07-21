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

using Launcher.App.Services;
using Launcher.App.Resources;
using Launcher.App.ViewModels.Download;
using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels;

public sealed class ResourcesProjectInstallViewModelTests
{
    [Fact]
    public void ModpackInstallTargetsUseExpectedIcons()
    {
        var newInstance = ResourcesModInstallTargetItemViewModel.CreateNewInstanceInstall(
            Strings.Resources_ModpackInstallTargetNewInstance);
        var server = ResourcesModInstallTargetItemViewModel.CreateServerInstall(
            Strings.Resources_ModpackInstallTargetServer);
        var saveAs = ResourcesModInstallTargetItemViewModel.CreateLocalDownload(
            Strings.Resources_ModpackInstallTargetLocal);

        Assert.Equal("main_menu_instance_download", newInstance.IconKey);
        Assert.Equal("server", server.IconKey);
        Assert.True(server.IsServerInstall);
        Assert.Equal("save_as", saveAs.IconKey);
    }

    [Fact]
    public async Task IntegrityFailureUsesLocalizedMessageInsteadOfGenericFailure()
    {
        var installation = new RecordingInstallationService { IntegrityFailure = true };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var statuses = new List<string>();
        var viewModel = CreateViewModel(installation, tasks, statuses.Add);

        await viewModel.InstallAsync(
            CreateVersionItem(ResourceProjectKind.ResourcePack),
            ResourcesModInstallTargetItemViewModel.CreateLocalDownload(),
            null);

        var task = Assert.Single(tasks.Tasks);
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Strings.Status_ResourceProjectIntegrityFailed, task.StatusMessage);
        Assert.Contains(Strings.Status_ResourceProjectIntegrityFailed, statuses);
        Assert.DoesNotContain("download failed", statuses);
    }

    [Fact]
    public async Task NewModpackInstallsRunConcurrentlyAndShowFeedbackForEachTask()
    {
        var installation = new ControllableInstallationService();
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var messages = new RecordingFloatingMessageService();
        var viewModel = CreateViewModel(installation, tasks, _ => { }, messages);
        var target = ResourcesModInstallTargetItemViewModel.CreateNewInstanceInstall("new instance");

        var first = viewModel.InstallAsync(CreateVersionItem(ResourceProjectKind.Modpack, "first"), target, null);
        var second = viewModel.InstallAsync(CreateVersionItem(ResourceProjectKind.Modpack, "second"), target, null);
        await installation.WaitForExecutionCountAsync(2);

        Assert.True(viewModel.IsInstalling);
        Assert.Equal(2, tasks.Tasks.Count);
        Assert.All(tasks.Tasks, task => Assert.Equal(DownloadTaskState.Running, task.State));
        Assert.Equal(2, messages.Messages.Count(message => message == "downloading"));

        installation.Complete("first", CreateSuccessfulModpackResult("first-instance"));
        installation.Fail("second", new InvalidOperationException("failure"));
        await Task.WhenAll(first, second);

        Assert.False(viewModel.IsInstalling);
        Assert.Equal(DownloadTaskState.Completed, tasks.Tasks.Single(task => task.Title == "Version first").State);
        Assert.Equal(DownloadTaskState.Failed, tasks.Tasks.Single(task => task.Title == "Version second").State);
    }

    [Fact]
    public async Task CancelingServerParentDirectoryDoesNotCreateTask()
    {
        var installation = new RecordingInstallationService();
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(
            installation,
            tasks,
            _ => { },
            filePickerService: new StubFilePickerService(null));

        await viewModel.InstallAsync(
            CreateVersionItem(ResourceProjectKind.Modpack),
            ResourcesModInstallTargetItemViewModel.CreateServerInstall("server"),
            new ResourcesModProjectItemViewModel(new ResourceProject
            {
                Kind = ResourceProjectKind.Modpack,
                Source = ResourceProjectSource.Modrinth,
                ProjectId = "project"
            }));

        Assert.Empty(installation.ExecutedRequests);
        Assert.Empty(tasks.Tasks);
    }

    [Fact]
    public async Task CancelingOneConcurrentModpackInstallDoesNotCancelTheOther()
    {
        var installation = new ControllableInstallationService();
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(installation, tasks, _ => { });
        var target = ResourcesModInstallTargetItemViewModel.CreateNewInstanceInstall("new instance");

        var first = viewModel.InstallAsync(CreateVersionItem(ResourceProjectKind.Modpack, "first"), target, null);
        var second = viewModel.InstallAsync(CreateVersionItem(ResourceProjectKind.Modpack, "second"), target, null);
        await installation.WaitForExecutionCountAsync(2);

        tasks.CancelTask(tasks.Tasks.Single(task => task.Title == "Version first"));
        await first;

        Assert.True(viewModel.IsInstalling);
        var remainingTask = Assert.Single(tasks.Tasks);
        Assert.Equal("Version second", remainingTask.Title);
        Assert.Equal(DownloadTaskState.Running, remainingTask.State);
        Assert.False(installation.IsCompleted("second"));

        installation.Complete("second", CreateSuccessfulModpackResult("second-instance"));
        await second;

        Assert.False(viewModel.IsInstalling);
        Assert.Equal(DownloadTaskState.Completed, remainingTask.State);
    }

    private static ResourcesProjectInstallViewModel CreateViewModel(
        IResourceProjectInstallationService installation,
        DownloadTasksPageViewModel tasks,
        Action<string> reportStatus,
        IFloatingMessageService? floatingMessageService = null,
        IFilePickerService? filePickerService = null)
    {
        var options = CreateOptions();
        return new ResourcesProjectInstallViewModel(
            options,
            installation,
            new ResourcesRequiredDependencyPlanner(null, options, null, reportStatus),
            filePickerService ?? new StubFilePickerService(),
            floatingMessageService,
            tasks,
            ImmediateUiDispatcher.Instance,
            NullLogger<ResourcesProjectInstallViewModel>.Instance,
            reportStatus);
    }

    private static ResourcesModVersionItemViewModel CreateVersionItem(
        ResourceProjectKind kind,
        string versionId = "version") => new(
        new ResourceProjectVersion
        {
            Kind = kind,
            VersionId = versionId,
            Name = $"Version {versionId}",
            FileName = kind is ResourceProjectKind.Mod ? "mod.jar" : "pack.zip"
        },
        null);

    private static ResourceProjectInstallationResult CreateSuccessfulModpackResult(string instanceId) => new(
        ModpackImportResult: ModpackImportResult.Success(new GameInstance
        {
            Id = instanceId,
            Name = instanceId,
            InstanceDirectory = instanceId
        }));

    private static ResourcesOnlineProjectPageOptions CreateOptions() => new(
        Kind: ResourceProjectKind.Mod,
        Title: "title",
        FallbackIconKey: "icon",
        ShowsLoaderFilters: true,
        AllVersionsText: "all versions",
        AllLoadersText: "all loaders",
        ProjectsLoadingText: "loading",
        ProjectsEmptyText: "empty",
        ProjectsLoadErrorText: "error",
        ProjectsLoadingMoreText: "loading more",
        ProjectsNoMoreText: "no more",
        ProjectsLoadMoreErrorText: "more error",
        CurseForgeMissingApiKeyText: "missing key",
        DetailsInfoSectionText: "details",
        InstallTargetSectionText: "targets",
        InstallTargetLocalText: "local",
        InstallTargetsLoadingText: "targets loading",
        InstallTargetsLoadErrorText: "targets error",
        VersionsLoadingText: "versions loading",
        VersionsEmptyText: "versions empty",
        VersionsEmptyLocalText: "local empty",
        VersionsFilterEmptyText: "filter empty",
        VersionsLoadErrorText: "versions error",
        VersionsLoadingMoreText: "versions more",
        VersionsNoMoreText: "versions no more",
        VersionsLoadMoreErrorText: "versions more error",
        VersionsAllTitleText: "all",
        DownloadDirectoryPickerTitle: "folder",
        DownloadingText: "downloading",
        DownloadingFormat: "downloading {0}",
        DownloadedFormat: "downloaded {0}",
        DownloadFailedText: "download failed",
        InstalledFormat: "installed {0}",
        InstallFailedText: "install failed",
        FileExistsMessageFormat: "exists {0}",
        TypeOptions: []);

    private sealed class RecordingInstallationService : IResourceProjectInstallationService
    {
        public bool TargetExists { get; init; }
        public bool WaitForCancellation { get; init; }
        public bool IntegrityFailure { get; init; }
        public string? TargetPath { get; init; }
        public TaskCompletionSource<bool> ExecuteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ResourceProjectInstallationRequest> ExecutedRequests { get; } = [];

        public Task<ResourceProjectInstallationPreparationResult> PrepareAsync(
            ResourceProjectInstallationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectInstallationPreparationResult(TargetExists, TargetPath));

        public async Task<ResourceProjectInstallationResult> ExecuteAsync(
            ResourceProjectInstallationRequest request,
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ExecutedRequests.Add(request);
            ExecuteStarted.TrySetResult(true);
            if (IntegrityFailure)
            {
                throw new ResourceProjectIntegrityException(
                    request.Version.VersionId,
                    ResourceProjectIntegrityFailureReason.HashMismatch,
                    ResourceFileHashAlgorithm.Sha512);
            }
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ResourceProjectInstallationResult();
        }
    }

    private sealed class ControllableInstallationService : IResourceProjectInstallationService
    {
        private readonly object syncRoot = new();
        private readonly Dictionary<string, TaskCompletionSource<ResourceProjectInstallationResult>> executions = [];
        private TaskCompletionSource<bool> executionCountChanged = CreateSignal();

        public Task<ResourceProjectInstallationPreparationResult> PrepareAsync(
            ResourceProjectInstallationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectInstallationPreparationResult(false));

        public Task<ResourceProjectInstallationResult> ExecuteAsync(
            ResourceProjectInstallationRequest request,
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<ResourceProjectInstallationResult> completion;
            lock (syncRoot)
            {
                completion = new TaskCompletionSource<ResourceProjectInstallationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                executions.Add(request.Version.VersionId, completion);
                executionCountChanged.TrySetResult(true);
                executionCountChanged = CreateSignal();
            }
            return completion.Task.WaitAsync(cancellationToken);
        }

        public async Task WaitForExecutionCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (true)
            {
                Task signal;
                lock (syncRoot)
                {
                    if (executions.Count >= count)
                        return;
                    signal = executionCountChanged.Task;
                }
                await signal.WaitAsync(timeout.Token);
            }
        }

        public void Complete(string versionId, ResourceProjectInstallationResult result)
        {
            lock (syncRoot)
                executions[versionId].TrySetResult(result);
        }

        public void Fail(string versionId, Exception exception)
        {
            lock (syncRoot)
                executions[versionId].TrySetException(exception);
        }

        public bool IsCompleted(string versionId)
        {
            lock (syncRoot)
                return executions[versionId].Task.IsCompleted;
        }

        private static TaskCompletionSource<bool> CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public List<string> Messages { get; } = [];

        public void Show(string message)
        {
            Messages.Add(message);
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class StubFilePickerService(string? folder = "target") : IFilePickerService
    {
        public string? PickMinecraftSkin() => null;
        public string? PickJavaExecutable() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickLaunchDiagnosticExportArchive(string instanceName) => null;
        public string? PickFolder(string title, string? initialDirectory = null) => folder;
    }
}
