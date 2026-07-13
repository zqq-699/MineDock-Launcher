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
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Download;

public sealed class DownloadViewModelTests
{
    [Fact]
    public async Task VersionListLoadsAndFiltersItsOwnCatalog()
    {
        using var viewModel = new DownloadVersionListViewModel(
            new StubGameVersionService(
            [
                new MinecraftVersionInfo("1.21", "release", false),
                new MinecraftVersionInfo("25w01a", "snapshot", false)
            ]),
            ImmediateUiDispatcher.Instance);

        await viewModel.EnsureVersionsLoadedAsync();

        var release = Assert.Single(viewModel.VisibleVersions);
        Assert.Equal("1.21", release.Name);
        Assert.Equal("release", viewModel.SelectedVersionCategory?.Id);
    }

    [Fact]
    public async Task SwitchingLoaderCancelsOlderVersionRequest()
    {
        var fabricGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fabric = new FakeLoaderProvider
        {
            Kind = LoaderKind.Fabric,
            LoaderVersions = [new LoaderVersionInfo("fabric-old")],
            WaitBeforeGetLoaderVersions = fabricGate.Task
        };
        var forge = new FakeLoaderProvider
        {
            Kind = LoaderKind.Forge,
            LoaderVersions = [new LoaderVersionInfo("forge-current")]
        };
        using var viewModel = new DownloadInstanceOptionsViewModel(
            new FakeGameInstanceService(),
            [fabric, forge],
            new DownloadInstanceNameTracker());
        await viewModel.PrepareAsync(CreateVersionItem("1.20.1"));

        viewModel.SelectedLoaderOption = viewModel.LoaderOptions.First(option => option.Kind is LoaderKind.Fabric);
        viewModel.SelectedLoaderOption = viewModel.LoaderOptions.First(option => option.Kind is LoaderKind.Forge);
        await WaitUntilAsync(() => viewModel.LoaderVersions.Count == 1);
        fabricGate.TrySetResult(true);
        await Task.Delay(100);

        Assert.Equal("forge-current", Assert.Single(viewModel.LoaderVersions).Version);
        Assert.False(viewModel.IsLoadingLoaderVersions);
    }

    [Fact]
    public async Task SuggestedNameIsRejectedWhenExistingInstanceUsesIt()
    {
        var service = new FakeGameInstanceService();
        service.CreatedInstances.Add(new GameInstance
        {
            Id = "existing",
            Name = "1.20.1",
            VersionName = "1.20.1"
        });
        using var viewModel = new DownloadInstanceOptionsViewModel(
            service,
            [],
            new DownloadInstanceNameTracker());

        await viewModel.PrepareAsync(CreateVersionItem("1.20.1"));

        Assert.True(viewModel.HasInstanceNameDuplicateMessage);
        Assert.False(viewModel.CanInstall);
    }

    [Fact]
    public async Task SelectingVersionShowsOptionsBeforeNameAvailabilityRefreshCompletes()
    {
        var nameRefreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService
        {
            WaitBeforeGetInstances = nameRefreshGate.Task
        };
        using var viewModel = CreatePageViewModel(
            instanceService,
            [new MinecraftVersionInfo("1.21", "release", false)]);
        await viewModel.EnsureVersionsLoadedAsync();

        viewModel.VersionList.SelectMinecraftVersionCommand.Execute(Assert.Single(viewModel.VersionList.VisibleVersions));
        await instanceService.GetInstancesStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(DownloadPageStep.InstanceOptions, viewModel.CurrentStep);

        viewModel.BackToVersionListCommand.Execute(null);
    }

    [Fact]
    public async Task SelectingCategoryFromOptionsReturnsToMatchingVersionList()
    {
        using var viewModel = CreatePageViewModel(
            new FakeGameInstanceService(),
            [
                new MinecraftVersionInfo("1.21", "release", false),
                new MinecraftVersionInfo("25w01a", "snapshot", false)
            ]);
        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.VersionList.SelectMinecraftVersionCommand.Execute(Assert.Single(viewModel.VersionList.VisibleVersions));
        await WaitUntilAsync(() => viewModel.CurrentStep is DownloadPageStep.InstanceOptions);

        var snapshot = viewModel.VersionList.VersionCategories.Single(category => category.Id == "snapshot");
        viewModel.VersionList.SelectVersionCategoryCommand.Execute(snapshot);

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.Same(snapshot, viewModel.VersionList.SelectedVersionCategory);
        Assert.Equal("25w01a", Assert.Single(viewModel.VersionList.VisibleVersions).Name);
        Assert.Null(viewModel.VersionList.SelectedMinecraftVersion);
    }

    [Fact]
    public async Task ParallelInstallationsAreTrackedUntilBothComplete()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeGameInstanceService { WaitBeforeCreate = release.Task };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.Zero);
        var tracker = new DownloadInstanceNameTracker();
        var viewModel = CreateInstallViewModel(service, tasks, tracker);
        var installedCount = 0;
        viewModel.InstanceInstalled += (_, _) => Interlocked.Increment(ref installedCount);

        var first = viewModel.InstallAsync(CreateInstallRequest("first"));
        var second = viewModel.InstallAsync(CreateInstallRequest("second"));
        await WaitUntilAsync(() => service.CreateCallCount == 2);
        Assert.True(viewModel.IsInstalling);

        release.TrySetResult(true);
        await Task.WhenAll(first, second);

        Assert.False(viewModel.IsInstalling);
        Assert.Equal(2, installedCount);
        Assert.Equal(2, service.CreateCallCount);
    }

    [Fact]
    public async Task DownloadPageTracksActualInstallTaskUntilRollbackOrCompletionFinishes()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeGameInstanceService { WaitBeforeCreate = release.Task };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.Zero);
        using var page = new DownloadPageViewModel(
            new StubGameVersionService([new MinecraftVersionInfo("1.20.1", "release", false)]),
            service,
            tasks,
            []);
        await page.EnsureVersionsLoadedAsync();
        page.VersionList.SelectMinecraftVersionCommand.Execute(Assert.Single(page.VersionList.VisibleVersions));
        await WaitUntilAsync(() => page.CurrentStep is DownloadPageStep.InstanceOptions);

        var installation = page.InstallCommand.ExecuteAsync(null);
        await service.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, tasks.TrackedBackgroundTaskCount);
        release.TrySetResult(true);
        await installation;
        await WaitUntilAsync(() => tasks.TrackedBackgroundTaskCount == 0);
    }

    [Fact]
    public void ActiveOperationStateFollowsRunningDownloadTasks()
    {
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var activityChanges = 0;
        tasks.ActivityChanged += (_, _) => activityChanges++;

        var task = tasks.BeginTask("install", "instance");

        Assert.True(tasks.HasActiveOperations);
        task.Complete("done");
        Assert.False(tasks.HasActiveOperations);
        Assert.True(activityChanges >= 2);
    }

    [Fact]
    public async Task BackgroundCleanupKeepsOperationActiveAfterTaskEntryIsRemoved()
    {
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = tasks.BeginTask("import", "pack");
        tasks.TrackBackgroundTask(cleanup.Task);

        tasks.CancelTask(task);

        Assert.Empty(tasks.Tasks);
        Assert.True(tasks.HasActiveOperations);

        cleanup.SetResult();
        await WaitUntilAsync(() => !tasks.HasActiveOperations);
    }

    [Fact]
    public async Task CancelingDownloadTaskCancelsInstallationWithoutFailureMessage()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeGameInstanceService { WaitBeforeCreate = release.Task };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromSeconds(1));
        var viewModel = CreateInstallViewModel(service, tasks, new DownloadInstanceNameTracker());
        var installation = viewModel.InstallAsync(CreateInstallRequest("cancel-me"));
        await service.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        tasks.CancelTask(Assert.Single(tasks.Tasks));
        await installation;

        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Empty(service.CreatedInstances);
    }

    [Fact]
    public async Task DuplicateNameFailureUsesFriendlyMessage()
    {
        var service = new FakeGameInstanceService
        {
            CreateException = new DuplicateGameInstanceNameException("duplicate")
        };
        var viewModel = CreateInstallViewModel(
            service,
            new DownloadTasksPageViewModel(TimeSpan.Zero),
            new DownloadInstanceNameTracker());

        await viewModel.InstallAsync(CreateInstallRequest("duplicate"));

        Assert.Equal(Strings.Status_DuplicateInstanceName, viewModel.InstallError);
        Assert.DoesNotContain("Exception", viewModel.InstallError, StringComparison.OrdinalIgnoreCase);
    }

    private static DownloadInstallViewModel CreateInstallViewModel(
        FakeGameInstanceService service,
        DownloadTasksPageViewModel tasks,
        DownloadInstanceNameTracker tracker)
    {
        return new DownloadInstallViewModel(
            service,
            tasks,
            tracker,
            ImmediateUiDispatcher.Instance,
            new RecordingFloatingMessageService(),
            NullLogger<DownloadInstallViewModel>.Instance);
    }

    private static DownloadPageViewModel CreatePageViewModel(
        FakeGameInstanceService instanceService,
        IReadOnlyList<MinecraftVersionInfo> versions)
    {
        return new DownloadPageViewModel(
            new StubGameVersionService(versions),
            instanceService,
            new DownloadTasksPageViewModel(TimeSpan.Zero),
            []);
    }

    private static DownloadInstallRequest CreateInstallRequest(string instanceName)
    {
        return new DownloadInstallRequest(
            "1.20.1",
            instanceName,
            LoaderKind.Vanilla,
            null,
            null,
            null,
            "Vanilla",
            DownloadSourcePreference.Auto,
            0);
    }

    private static DownloadMinecraftVersionItem CreateVersionItem(string version)
    {
        return new DownloadMinecraftVersionItem(new MinecraftVersionInfo(version, "release", false));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class StubGameVersionService(IReadOnlyList<MinecraftVersionInfo> versions) : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromResult(versions);
        }
    }

    private sealed class RecordingFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public void Show(string message)
        {
            MessageRequested?.Invoke(message);
        }
    }
}
