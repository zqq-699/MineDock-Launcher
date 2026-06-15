using Launcher.App.Resources;
using Launcher.App.ViewModels;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests;

public sealed class DownloadPageViewModelTests
{
    [Fact]
    public async Task DownloadPageShowsOnlyReleaseVersionsForReleaseCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["1.21.4", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageDoesNotRescanInstancesAfterVersionsAreLoaded()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(1, instanceService.GetInstancesCallCount);
    }

    [Fact]
    public async Task DownloadPageRefreshesInstanceNamesWhenEnteringInstanceOptions()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(new GameInstance { Name = "1.20.1", VersionName = "1.20.1" });
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        instanceService.CreatedInstances.Clear();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        Assert.Equal(2, instanceService.GetInstancesCallCount);
        Assert.False(viewModel.HasInstanceNameDuplicateMessage);
        Assert.Empty(viewModel.InstanceNameDuplicateMessage);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageShowsOnlyOldBetaVersionsForBetaCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("b1.7.3", "old_beta", false, new DateTimeOffset(2011, 7, 8, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("b1.6.6", "old_beta", false, new DateTimeOffset(2011, 5, 31, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("a1.2.6", "old_alpha", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "old_beta"));

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["b1.7.3", "b1.6.6"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.All(viewModel.VisibleVersions, version =>
        {
            Assert.True(version.IsBeta);
            Assert.Equal(Strings.Download_BetaCategory, version.TypeLabel);
            Assert.Equal("/Assets/Icons/block/craftingtable_block.png", version.IconSource);
        });
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageShowsOnlyOldAlphaVersionsForAlphaCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("b1.7.3", "old_beta", false),
            new MinecraftVersionInfo("a1.2.6", "old_alpha", false, new DateTimeOffset(2010, 12, 3, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("a1.1.2", "old_alpha", false, new DateTimeOffset(2010, 9, 18, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("24w45a", "Snapshot", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "old_alpha"));

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["a1.2.6", "a1.1.2"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.All(viewModel.VisibleVersions, version =>
        {
            Assert.True(version.IsAlpha);
            Assert.Equal(Strings.Download_AlphaCategory, version.TypeLabel);
            Assert.Equal("/Assets/Icons/block/stone_block.png", version.IconSource);
        });
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageSearchFiltersOldBetaVersions()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("b1.7.3", "old_beta", false),
            new MinecraftVersionInfo("b1.6.6", "old_beta", false),
            new MinecraftVersionInfo("a1.2.6", "old_alpha", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "old_beta"));
        viewModel.VersionSearchQuery = "1.7";

        Assert.Equal(["b1.7.3"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.False(viewModel.HasVersionEmptyMessage);
    }

    [Fact]
    public async Task DownloadPageShowsOnlySnapshotVersionsForSnapshotCategory()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false, new DateTimeOffset(2024, 10, 30, 0, 0, 0, TimeSpan.Zero)),
            new MinecraftVersionInfo("24w44a", "Snapshot", false, new DateTimeOffset(2024, 11, 06, 0, 0, 0, TimeSpan.Zero))
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.True(viewModel.HasVisibleVersions);
        Assert.Equal(["24w44a", "24w45a"], viewModel.VisibleVersions.Select(version => version.Name));
        Assert.All(viewModel.VisibleVersions, version =>
        {
            Assert.True(version.IsSnapshot);
            Assert.Equal("\u5feb\u7167\u7248", version.TypeLabel);
            Assert.Equal("/Assets/Icons/block/dirt_block.png", version.IconSource);
        });
    }

    [Fact]
    public async Task DownloadPageExposesAllFilteredSnapshotVersions()
    {
        var snapshots = Enumerable
            .Range(0, 130)
            .Select(index => new MinecraftVersionInfo(
                $"24w{index:00}a",
                "Snapshot",
                false,
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index)))
            .ToList();
        var viewModel = CreateDownloadPageViewModel(new FakeGameVersionService(snapshots));

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.Equal(130, viewModel.VisibleVersions.Count);
        Assert.Equal("24w129a", viewModel.VisibleVersions.First().Name);
        Assert.Equal("24w00a", viewModel.VisibleVersions.Last().Name);
    }

    [Fact]
    public async Task DownloadPageSelectsVersionItem()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Last();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(version);

        Assert.Same(version, viewModel.SelectedMinecraftVersion);
        Assert.True(version.IsSelected);
        Assert.False(viewModel.VisibleVersions.First().IsSelected);
        Assert.Equal(DownloadPageStep.InstanceOptions, viewModel.CurrentStep);
    }

    [Fact]
    public async Task DownloadPageSearchFiltersReleaseVersions()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.6", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.VersionSearchQuery = "1.20";

        Assert.Equal(["1.20.6", "1.20.1"], viewModel.VisibleVersions.Select(version => version.Name));
    }

    [Fact]
    public async Task DownloadPageReselectsCurrentCategoryToRefreshContent()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Last());
        var previousRefreshToken = viewModel.ContentRefreshToken;
        var previousEntranceAnimationToken = viewModel.ListEntranceAnimationToken;

        viewModel.SelectVersionCategoryCommand.Execute(viewModel.SelectedVersionCategory);

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.ContentRefreshToken > previousRefreshToken);
        Assert.Equal(previousEntranceAnimationToken, viewModel.ListEntranceAnimationToken);
        Assert.True(viewModel.HasVisibleVersions);
        Assert.True(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
        Assert.False(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageRequestsListEntranceAnimationOnlyForInitialLoadAndCategorySwitch()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.21.4", "Release", false),
            new MinecraftVersionInfo("24w45a", "Snapshot", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        Assert.Equal(0, viewModel.ListEntranceAnimationToken);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(1, viewModel.ListEntranceAnimationToken);

        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await instanceService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.Equal(2, viewModel.ListEntranceAnimationToken);

        allowCreate.SetResult(true);
        await installTask;

        Assert.Equal(2, viewModel.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task DownloadPageCannotEnterInstanceOptionsWithoutSelectedVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.IsVersionListStep);
        Assert.False(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageEntersInstanceOptionsWithSelectedReleaseDefaults()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Single();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(version);

        Assert.Equal(DownloadPageStep.InstanceOptions, viewModel.CurrentStep);
        Assert.True(viewModel.IsInstanceOptionsStep);
        Assert.Equal("1.20.1", viewModel.InstanceName);
        Assert.Equal("1.20.1", viewModel.PageTitle);
        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.PageTitleIconSource);
        Assert.Equal([LoaderKind.Vanilla, LoaderKind.Fabric, LoaderKind.Forge], viewModel.LoaderOptions.Select(option => option.Kind));
        Assert.Equal(LoaderKind.Vanilla, viewModel.SelectedLoaderOption?.Kind);
        Assert.True(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IsSelected);
        Assert.Equal("/Assets/Icons/block/grass_block.png", viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IconSource);
    }

    [Fact]
    public async Task DownloadPageShowsDuplicateMessageForExistingInstanceName()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(new GameInstance { Name = "Existing Display Name", VersionName = "1.20.1" });
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        Assert.True(viewModel.HasInstanceNameDuplicateMessage);
        Assert.Equal("\u5df2\u5b58\u5728\u540c\u540d\u6e38\u620f", viewModel.InstanceNameDuplicateMessage);
        Assert.False(viewModel.InstallCommand.CanExecute(null));

        viewModel.InstanceName = "1.20.1 Copy";

        Assert.False(viewModel.HasInstanceNameDuplicateMessage);
        Assert.Empty(viewModel.InstanceNameDuplicateMessage);
        Assert.True(viewModel.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPageUsesSnapshotIconForInstanceOptionsTitle()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false),
            new MinecraftVersionInfo("24w44a", "Snapshot", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        viewModel.SelectVersionCategoryCommand.Execute(viewModel.VersionCategories.Single(category => category.Id == "snapshot"));
        var version = viewModel.VisibleVersions.Single();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(version);

        Assert.Equal("24w44a", viewModel.PageTitle);
        Assert.Equal("/Assets/Icons/block/dirt_block.png", viewModel.PageTitleIconSource);
    }

    [Fact]
    public async Task DownloadPageSelectsLoaderOption()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        var fabric = viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric);
        viewModel.SelectLoaderOptionCommand.Execute(fabric);

        Assert.Same(fabric, viewModel.SelectedLoaderOption);
        Assert.True(fabric.IsSelected);
        Assert.False(viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Vanilla).IsSelected);
    }

    [Fact]
    public async Task DownloadPageBackToVersionListClearsSelectedVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();
        var version = viewModel.VisibleVersions.Single();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(version);
        viewModel.BackToVersionListCommand.Execute(null);

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.Null(viewModel.SelectedMinecraftVersion);
        Assert.False(version.IsSelected);
        Assert.All(viewModel.VisibleVersions, item => Assert.False(item.IsSelected));
        Assert.False(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
        Assert.Equal("\u6b63\u5f0f\u7248", viewModel.PageTitle);
        Assert.Null(viewModel.PageTitleIconSource);
    }

    [Fact]
    public async Task DownloadPageInstallCommandRequiresSelectedVanillaVersion()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var viewModel = CreateDownloadPageViewModel(service);

        await viewModel.EnsureVersionsLoadedAsync();

        Assert.False(viewModel.InstallCommand.CanExecute(null));

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        Assert.True(viewModel.InstallCommand.CanExecute(null));

        viewModel.IsInstalling = true;
        Assert.True(viewModel.InstallCommand.CanExecute(null));

        viewModel.IsInstalling = false;
        var fabric = viewModel.LoaderOptions.Single(option => option.Kind == LoaderKind.Fabric);
        viewModel.SelectLoaderOptionCommand.Execute(fabric);

        Assert.False(viewModel.InstallCommand.CanExecute(null));
        Assert.True(viewModel.HasInstallStatus);
    }

    [Fact]
    public async Task DownloadPageInstallCreatesVanillaInstance()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var instanceService = new FakeGameInstanceService();
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);
        GameInstance? installedInstance = null;
        viewModel.InstanceInstalled += (_, instance) => installedInstance = instance;

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());
        viewModel.InstanceName = "My Vanilla";
        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal("1.20.1", instanceService.LastMinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, instanceService.LastLoader);
        Assert.Null(instanceService.LastLoaderVersion);
        Assert.Equal("My Vanilla", instanceService.LastName);
        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Equal(100, viewModel.InstallProgressPercent);
        Assert.Same(instanceService.CreatedInstances.Single(), installedInstance);
        Assert.Contains("\u5df2\u5b89\u88c5", viewModel.InstallStatusMessage);

        var task = Assert.Single(tasksPage.Tasks);
        Assert.True(tasksPage.HasTasks);
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Contains("1.20.1", task.Title);
        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
    }

    [Fact]
    public async Task DownloadPageInstallReturnsToVersionListWhenTaskStarts()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await instanceService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(DownloadPageStep.VersionList, viewModel.CurrentStep);
        Assert.True(viewModel.HasVisibleVersions);
        Assert.True(viewModel.GoToInstanceOptionsCommand.CanExecute(null));
        Assert.False(viewModel.InstallCommand.CanExecute(null));

        allowCreate.SetResult(true);
        await installTask;
    }

    [Fact]
    public async Task DownloadPageCancelInstallTaskStopsInstallAndRemovesTask()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await instanceService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var taskItem = Assert.Single(tasksPage.Tasks);

        tasksPage.CancelTaskCommand.Execute(taskItem);
        await installTask;

        Assert.True(taskItem.IsCancellationRequested);
        Assert.Empty(tasksPage.Tasks);
        Assert.Empty(instanceService.CreatedInstances);
        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Empty(viewModel.InstallStatusMessage);
    }

    [Fact]
    public async Task DownloadPageAllowsConcurrentInstalls()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false),
            new MinecraftVersionInfo("1.20.2", "Release", false)
        ]);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceService = new FakeGameInstanceService { WaitBeforeCreate = allowCreate.Task };
        var tasksPage = new DownloadTasksPageViewModel();
        var viewModel = CreateDownloadPageViewModel(service, instanceService, tasksPage);

        await viewModel.EnsureVersionsLoadedAsync();

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions[0]);
        viewModel.InstanceName = "First Install";
        var firstInstall = viewModel.InstallCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => instanceService.CreateCallCount == 1);

        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions[1]);
        viewModel.InstanceName = "Second Install";

        Assert.True(viewModel.InstallCommand.CanExecute(null));

        var secondInstall = viewModel.InstallCommand.ExecuteAsync(null);
        await TestAsync.WaitForAsync(() => instanceService.CreateCallCount == 2);

        Assert.True(viewModel.IsInstalling);
        Assert.Equal(2, tasksPage.Tasks.Count);
        Assert.All(tasksPage.Tasks, task => Assert.Equal(DownloadTaskState.Running, task.State));

        allowCreate.SetResult(true);
        await Task.WhenAll(firstInstall, secondInstall);

        Assert.False(viewModel.IsInstalling);
        Assert.Equal(2, instanceService.CreatedInstances.Count);
        Assert.Contains(instanceService.CreatedInstances, instance => instance.Name == "First Install");
        Assert.Contains(instanceService.CreatedInstances, instance => instance.Name == "Second Install");
        Assert.All(tasksPage.Tasks, task => Assert.Equal(DownloadTaskState.Completed, task.State));
    }

    [Fact]
    public async Task DownloadPageInstallShowsErrorAndPropagatesFailure()
    {
        var service = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("1.20.1", "Release", false)
        ]);
        var expected = new InvalidOperationException("network down");
        var instanceService = new FakeGameInstanceService { CreateException = expected };
        var viewModel = CreateDownloadPageViewModel(service, instanceService);

        await viewModel.EnsureVersionsLoadedAsync();
        await viewModel.SelectMinecraftVersionCommand.ExecuteAsync(viewModel.VisibleVersions.Single());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.InstallCommand.ExecuteAsync(null));

        Assert.Same(expected, actual);
        Assert.False(viewModel.IsInstalling);
        Assert.True(viewModel.HasInstallError);
        Assert.Equal(Strings.Status_InstallFailed, viewModel.InstallError);
    }


    private static DownloadPageViewModel CreateDownloadPageViewModel(
        IGameVersionService gameVersionService,
        FakeGameInstanceService? instanceService = null,
        DownloadTasksPageViewModel? tasksPage = null)
    {
        return new DownloadPageViewModel(
            gameVersionService,
            instanceService ?? new FakeGameInstanceService(),
            tasksPage ?? new DownloadTasksPageViewModel());
    }
}
