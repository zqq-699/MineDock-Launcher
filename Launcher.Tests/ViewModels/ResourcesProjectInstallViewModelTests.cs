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
    public async Task LocalDownloadUsesDirectoryTargetAndCompletesTask()
    {
        var installation = new RecordingInstallationService();
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var statuses = new List<string>();
        var viewModel = CreateViewModel(installation, tasks, statuses.Add);

        await viewModel.InstallAsync(
            CreateVersionItem(ResourceProjectKind.ResourcePack),
            ResourcesModInstallTargetItemViewModel.CreateLocalDownload(),
            null);

        var request = Assert.Single(installation.ExecutedRequests);
        Assert.Equal(ResourceProjectInstallationTargetKind.LocalDirectory, request.TargetKind);
        Assert.Equal("target", request.TargetDirectory);
        Assert.Equal(DownloadTaskState.Completed, Assert.Single(tasks.Tasks).State);
        Assert.Contains(statuses, status => status.StartsWith("downloaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingTargetOpensFileExistsDialogWithoutExecuting()
    {
        var installation = new RecordingInstallationService { TargetExists = true };
        var viewModel = CreateViewModel(
            installation,
            new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1)),
            _ => { });

        await viewModel.InstallAsync(
            CreateVersionItem(ResourceProjectKind.Mod),
            ResourcesModInstallTargetItemViewModel.FromInstance(new GameInstance
            {
                Id = "instance",
                InstanceDirectory = "instance"
            }),
            null);

        Assert.True(viewModel.IsFileExistsDialogOpen);
        Assert.Empty(installation.ExecutedRequests);
    }

    [Fact]
    public async Task CancelingDownloadTaskCancelsActiveInstallation()
    {
        var installation = new RecordingInstallationService { WaitForCancellation = true };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var viewModel = CreateViewModel(installation, tasks, _ => { });
        var operation = viewModel.InstallAsync(
            CreateVersionItem(ResourceProjectKind.ResourcePack),
            ResourcesModInstallTargetItemViewModel.CreateLocalDownload(),
            null);
        await installation.ExecuteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        tasks.CancelTask(Assert.Single(tasks.Tasks));
        await operation;

        Assert.False(viewModel.IsInstalling);
        Assert.Empty(tasks.Tasks);
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

    private static ResourcesProjectInstallViewModel CreateViewModel(
        RecordingInstallationService installation,
        DownloadTasksPageViewModel tasks,
        Action<string> reportStatus)
    {
        var options = CreateOptions();
        return new ResourcesProjectInstallViewModel(
            options,
            installation,
            new ResourcesRequiredDependencyPlanner(null, options, null, reportStatus),
            new StubFilePickerService(),
            null,
            tasks,
            ImmediateUiDispatcher.Instance,
            NullLogger<ResourcesProjectInstallViewModel>.Instance,
            reportStatus);
    }

    private static ResourcesModVersionItemViewModel CreateVersionItem(ResourceProjectKind kind) => new(
        new ResourceProjectVersion
        {
            Kind = kind,
            VersionId = "version",
            Name = "Version",
            FileName = kind is ResourceProjectKind.Mod ? "mod.jar" : "pack.zip"
        },
        null);

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
        public TaskCompletionSource<bool> ExecuteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ResourceProjectInstallationRequest> ExecutedRequests { get; } = [];

        public Task<ResourceProjectInstallationPreparationResult> PrepareAsync(
            ResourceProjectInstallationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectInstallationPreparationResult(TargetExists));

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

    private sealed class StubFilePickerService : IFilePickerService
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
        public string? PickFolder(string title, string? initialDirectory = null) => "target";
    }
}
